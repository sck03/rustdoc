using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting
{
    /// <summary>
    /// Owns the multi-user template boundary. Built-in file templates are not copied
    /// into this table; only user-created templates are stored here.
    /// </summary>
    public sealed class UserReportTemplateService : IUserReportTemplateService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public UserReportTemplateService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<IReadOnlyList<UserReportTemplateRecord>> ListAsync(
            ReportDocumentType reportType,
            bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            string type = reportType.ToString();
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                .Where(item => item.ReportType == type);
            if (!includeInactive)
            {
                query = query.Where(item => item.IsActive);
            }

            bool canEditAll = BusinessDataAccessScope.CanViewAllBusinessData(_accessScope.CurrentUser);
            int currentUserId = _accessScope.CurrentUser?.Id ?? 0;
            return await query
                .OrderByDescending(item => item.IsShared)
                .ThenBy(item => item.Name)
                .Select(item => new UserReportTemplateRecord(
                    item.Id,
                    item.ReportType,
                    item.Name,
                    item.ContentHtml,
                    item.IsActive,
                    item.IsShared,
                    item.ShareScope,
                    item.VersionNumber,
                    canEditAll || item.OwnerUserId == currentUserId,
                    item.OwnerUserId))
                .ToListAsync(cancellationToken);
        }

        public async Task<UserReportTemplateRecord> SaveAsync(
            UserReportTemplateSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Enum.TryParse<ReportDocumentType>(request.ReportType, true, out var reportType))
            {
                throw new ArgumentException("报表类型无效。");
            }

            string name = Required(request.Name, "模板名称");
            string content = request.ContentHtml ?? string.Empty;
            string shareScope = UserReportTemplateShareScope.Normalize(request.ShareScope);
            ValidateDataDomain(reportType, content);
            if (name.Length > 150 || content.Length > 2_000_000)
            {
                throw new ArgumentException("报表模板名称或内容超过允许长度。");
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            UserReportTemplate entity;
            bool isNew = request.Id <= 0;
            if (isNew)
            {
                entity = new UserReportTemplate
                {
                    ReportType = reportType.ToString(),
                    OwnerUserId = _accessScope.CurrentUser?.Id,
                    DepartmentId = _accessScope.CurrentUser?.DepartmentId?.Trim() ?? string.Empty,
                    CompanyScope = _accessScope.CurrentUser?.CompanyScope?.Trim() ?? string.Empty,
                    ShareScope = shareScope
                };
                _accessScope.ApplyOwner(entity);
                await context.UserReportTemplates.AddAsync(entity, cancellationToken);
            }
            else
            {
                entity = await _accessScope.ApplyOwnedUserReportTemplateScope(context.UserReportTemplates)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("报表模板不存在或无权修改。");
                if (!string.Equals(entity.ReportType, reportType.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("不能修改报表模板类型。");
                }

                if (request.ExpectedVersion > 0 && entity.VersionNumber != request.ExpectedVersion)
                {
                    throw new UserReportTemplateConcurrencyException(
                        "模板已被其他操作更新，请刷新后确认差异再保存。");
                }
            }

            int currentUserId = _accessScope.CurrentUser?.Id ?? 0;
            bool duplicate = await _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                .AnyAsync(item => item.Id != entity.Id && item.ReportType == reportType.ToString() &&
                                  item.Name == name && item.OwnerUserId == currentUserId, cancellationToken);
            if (duplicate)
            {
                throw new ArgumentException("你已经拥有同名报表模板。");
            }

            // Publishing is an explicit action. A normal user can share their own
            // template, but cannot change ownership or overwrite another user's item.
            entity.Name = name;
            entity.ContentHtml = content;
            entity.IsActive = request.IsActive;
            entity.ShareScope = shareScope;
            entity.IsShared = !string.Equals(shareScope, UserReportTemplateShareScope.Private, StringComparison.OrdinalIgnoreCase);
            entity.VersionNumber = isNew ? 1 : Math.Max(1, entity.VersionNumber + 1);
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await context.UserReportTemplateVersions.AddAsync(new UserReportTemplateVersion
            {
                Template = entity,
                VersionNumber = entity.VersionNumber,
                ChangeType = isNew ? "创建" : "更新",
                Name = entity.Name,
                ContentHtml = entity.ContentHtml,
                IsActive = entity.IsActive,
                IsShared = entity.IsShared,
                ShareScope = entity.ShareScope,
                ChangedBy = _accessScope.CurrentUser?.Username ?? string.Empty,
                CreatedAt = entity.UpdatedAt
            }, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return ToRecord(entity, true);
        }

        public async Task<IReadOnlyList<UserReportTemplateVersionRecord>> ListVersionsAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var template = await _accessScope.ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                .Where(item => item.Id == id)
                .Select(item => new { item.Id, item.OwnerUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (template == null)
            {
                return [];
            }

            bool canRestore = BusinessDataAccessScope.CanViewAllBusinessData(_accessScope.CurrentUser) ||
                              template.OwnerUserId == (_accessScope.CurrentUser?.Id ?? 0);
            return await context.UserReportTemplateVersions.AsNoTracking()
                .Where(item => item.UserReportTemplateId == id)
                .OrderByDescending(item => item.VersionNumber)
                .Select(item => new UserReportTemplateVersionRecord(
                    item.Id,
                    item.UserReportTemplateId,
                    item.VersionNumber,
                    item.ChangeType,
                    item.Name,
                    item.ContentHtml,
                    item.IsActive,
                    item.IsShared,
                    item.ShareScope,
                    item.ChangedBy,
                    item.CreatedAt,
                    canRestore))
                .ToListAsync(cancellationToken);
        }

        public async Task<UserReportTemplateRecord> RestoreVersionAsync(
            int id,
            int versionNumber,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplyOwnedUserReportTemplateScope(context.UserReportTemplates)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("报表模板不存在或无权恢复。");
            var source = await context.UserReportTemplateVersions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserReportTemplateId == id && item.VersionNumber == versionNumber, cancellationToken)
                ?? throw new KeyNotFoundException("报表模板历史版本不存在。");
            if (source.VersionNumber == entity.VersionNumber)
            {
                return ToRecord(entity, true);
            }

            ValidateDataDomain(Enum.Parse<ReportDocumentType>(entity.ReportType), source.ContentHtml);
            entity.Name = source.Name;
            entity.ContentHtml = source.ContentHtml;
            entity.IsActive = source.IsActive;
            entity.IsShared = source.IsShared;
            entity.ShareScope = source.ShareScope;
            entity.VersionNumber = Math.Max(1, entity.VersionNumber + 1);
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await context.UserReportTemplateVersions.AddAsync(new UserReportTemplateVersion
            {
                Template = entity,
                VersionNumber = entity.VersionNumber,
                ChangeType = $"恢复 V{source.VersionNumber}",
                Name = entity.Name,
                ContentHtml = entity.ContentHtml,
                IsActive = entity.IsActive,
                IsShared = entity.IsShared,
                ShareScope = entity.ShareScope,
                ChangedBy = _accessScope.CurrentUser?.Username ?? string.Empty,
                CreatedAt = entity.UpdatedAt
            }, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return ToRecord(entity, true);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplyOwnedUserReportTemplateScope(context.UserReportTemplates)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null)
            {
                return false;
            }

            context.UserReportTemplates.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        private static UserReportTemplateRecord ToRecord(UserReportTemplate item, bool canEdit) =>
            new(item.Id, item.ReportType, item.Name, item.ContentHtml, item.IsActive,
                item.IsShared, item.ShareScope, item.VersionNumber, canEdit, item.OwnerUserId);

        private static string Required(string value, string field) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException($"{field}不能为空。")
                : value.Trim();

        private static void ValidateDataDomain(ReportDocumentType reportType, string content)
        {
            content ??= string.Empty;
            if (reportType == ReportDocumentType.PaymentVoucher)
            {
                string[] documentTokens = ["Invoice.", "Customer.", "Exporter.", "item.", "Invoice.Items"];
                if (documentTokens.Any(token => content.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("付款报销模板不能使用报关单证数据字段。");
                }

                return;
            }

            if (content.Contains("Payment.", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("报关单证模板不能使用付款报销数据字段。");
            }
        }
    }
}

using System.Net;
using System.Text.RegularExpressions;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.EmailTemplates
{
    public sealed class EmailTemplateService : IEmailTemplateService
    {
        private static readonly IReadOnlyList<EmailTemplateVariableRecord> Variables =
        [
            new("CustomerName", "{{CustomerName}}", "客户名称", "Acme Trading"),
            new("ContactName", "{{ContactName}}", "联系人", "Alice"),
            new("CompanyName", "{{CompanyName}}", "本公司名称", "示例外贸有限公司"),
            new("ProductName", "{{ProductName}}", "产品名称", "Sample Product"),
            new("QuotationNo", "{{QuotationNo}}", "报价单号", "QT-20260712-001"),
            new("SenderName", "{{SenderName}}", "发件人姓名", "业务员"),
            new("Today", "{{Today}}", "当前日期", "2026-07-12")
        ];
        private static readonly Regex TokenPattern = new(@"\{\{[A-Za-z][A-Za-z0-9]*\}\}", RegexOptions.Compiled);
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public EmailTemplateService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<IReadOnlyList<EmailTemplateRecord>> ListAsync(
            string keyword, string category, bool includeInactive, CancellationToken cancellationToken = default)
        {
            keyword = Clean(keyword);
            category = Clean(category);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyEmailTemplateScope(context.EmailTemplates.AsNoTracking());
            if (keyword.Length > 0)
                query = query.Where(item => item.Name.Contains(keyword) || item.Subject.Contains(keyword) || item.BodyHtml.Contains(keyword));
            if (category.Length > 0) query = query.Where(item => item.Category == category);
            if (!includeInactive) query = query.Where(item => item.IsActive);
            bool canEditAll = !_accessScope.ShouldFilterBusinessData();
            int currentUserId = _accessScope.CurrentUser?.Id ?? 0;
            return await query.OrderBy(item => item.Category).ThenBy(item => item.Name)
                .Select(item => new EmailTemplateRecord(item.Id, item.Name, item.Category, item.Subject, item.BodyHtml,
                    item.IsActive, item.IsShared, item.VersionNumber, canEditAll || item.OwnerUserId == currentUserId))
                .ToListAsync(cancellationToken);
        }

        public async Task<EmailTemplateRecord> SaveAsync(EmailTemplateSaveRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string name = Required(request.Name, "模板名称");
            string category = string.IsNullOrWhiteSpace(request.Category) ? "通用" : request.Category.Trim();
            string subject = Clean(request.Subject);
            string bodyHtml = Clean(request.BodyHtml);
            if (name.Length > 150 || category.Length > 50 || subject.Length > 300 || bodyHtml.Length > 10000)
                throw new ArgumentException("邮件模板字段超过允许长度。");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            EmailTemplate entity;
            bool isNew = request.Id <= 0;
            if (request.Id > 0)
            {
                entity = await _accessScope.ApplyOwnedEmailTemplateScope(context.EmailTemplates)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("邮件模板不存在或无权访问。");
                if (request.ExpectedVersion <= 0)
                    throw new BusinessConcurrencyException("保存现有邮件模板时必须提供版本号，请刷新后重试。");
                if (entity.VersionNumber != request.ExpectedVersion)
                    throw new BusinessConcurrencyException("该邮件模板已被其他用户修改，请刷新后重试。");
                context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
            }
            else
            {
                entity = new EmailTemplate();
                _accessScope.ApplyOwner(entity);
                await context.EmailTemplates.AddAsync(entity, cancellationToken);
            }
            bool duplicate = await _accessScope.ApplyOwnedEmailTemplateScope(context.EmailTemplates.AsNoTracking())
                .AnyAsync(item => item.Id != entity.Id && item.Name == name && item.Category == category, cancellationToken);
            if (duplicate) throw new ArgumentException("同一分类下已存在同名邮件模板。");
            bool changed = isNew || HasChanges(entity, name, category, subject, bodyHtml, request.IsActive, request.IsShared);
            if (!changed) return ToRecord(entity);
            var now = DateTimeOffset.UtcNow;
            entity.Name = name;
            entity.Category = category;
            entity.Subject = subject;
            entity.BodyHtml = bodyHtml;
            entity.IsActive = request.IsActive;
            entity.IsShared = request.IsShared;
            entity.VersionNumber = isNew ? 1 : Math.Max(1, entity.VersionNumber + 1);
            entity.UpdatedAt = now;
            await context.EmailTemplateVersions.AddAsync(CreateVersion(entity, isNew ? "创建" : "更新", now), cancellationToken);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该邮件模板已被其他用户修改，请刷新后重试。", exception);
            }
            return ToRecord(entity);
        }

        public async Task<IReadOnlyList<EmailTemplateVersionRecord>> ListVersionsAsync(
            int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var template = await _accessScope.ApplyEmailTemplateScope(context.EmailTemplates.AsNoTracking())
                .Where(item => item.Id == id)
                .Select(item => new { item.Id, item.OwnerUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (template == null) return [];
            bool canRestore = !_accessScope.ShouldFilterBusinessData() || template.OwnerUserId == (_accessScope.CurrentUser?.Id ?? 0);
            return await context.EmailTemplateVersions.AsNoTracking()
                .Where(item => item.EmailTemplateId == id)
                .OrderByDescending(item => item.VersionNumber)
                .Select(item => new EmailTemplateVersionRecord(item.Id, item.EmailTemplateId, item.VersionNumber,
                    item.ChangeType, item.Name, item.Category, item.Subject, item.BodyHtml, item.IsActive,
                    item.IsShared, item.ChangedBy, item.CreatedAt, canRestore))
                .ToListAsync(cancellationToken);
        }

        public async Task<EmailTemplateRecord> RestoreVersionAsync(
            int id, int versionNumber, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplyOwnedEmailTemplateScope(context.EmailTemplates)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("邮件模板不存在或无权访问。");
            var source = await context.EmailTemplateVersions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.EmailTemplateId == id && item.VersionNumber == versionNumber, cancellationToken)
                ?? throw new KeyNotFoundException("邮件模板历史版本不存在。");
            if (source.VersionNumber == entity.VersionNumber) return ToRecord(entity);
            int expectedVersion = entity.VersionNumber;
            context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
            bool duplicate = await _accessScope.ApplyOwnedEmailTemplateScope(context.EmailTemplates.AsNoTracking())
                .AnyAsync(item => item.Id != id && item.Name == source.Name && item.Category == source.Category, cancellationToken);
            if (duplicate) throw new ArgumentException("恢复后的分类和名称与现有邮件模板重复。");

            entity.Name = source.Name;
            entity.Category = source.Category;
            entity.Subject = source.Subject;
            entity.BodyHtml = source.BodyHtml;
            entity.IsActive = source.IsActive;
            entity.IsShared = source.IsShared;
            entity.VersionNumber = Math.Max(1, entity.VersionNumber + 1);
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await context.EmailTemplateVersions.AddAsync(
                CreateVersion(entity, $"恢复 V{source.VersionNumber}", entity.UpdatedAt), cancellationToken);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该邮件模板已被其他用户修改，请刷新后重试。", exception);
            }
            return ToRecord(entity);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await _accessScope.ApplyOwnedEmailTemplateScope(context.EmailTemplates)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (entity == null) return false;
            context.EmailTemplates.Remove(entity);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException("该邮件模板已被其他用户修改，请刷新后重试。", exception);
            }
            return true;
        }

        public IReadOnlyList<EmailTemplateVariableRecord> ListVariables() => Variables;

        public EmailTemplatePreview Preview(EmailTemplatePreviewRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var supplied = request.Variables ?? new Dictionary<string, string>();
            string subject = request.Subject ?? string.Empty;
            string body = request.BodyHtml ?? string.Empty;
            foreach (var variable in Variables)
            {
                supplied.TryGetValue(variable.Key, out string value);
                value ??= string.Empty;
                subject = subject.Replace(variable.Token, value, StringComparison.Ordinal);
                body = body.Replace(variable.Token, WebUtility.HtmlEncode(value), StringComparison.Ordinal);
            }
            var unresolved = TokenPattern.Matches(subject + "\n" + body).Select(match => match.Value)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray();
            return new EmailTemplatePreview(subject, body, unresolved);
        }

        private static EmailTemplateRecord ToRecord(EmailTemplate item) =>
            new(item.Id, item.Name, item.Category, item.Subject, item.BodyHtml, item.IsActive, item.IsShared,
                item.VersionNumber, true);
        private EmailTemplateVersion CreateVersion(EmailTemplate item, string changeType, DateTimeOffset createdAt) => new()
        {
            EmailTemplateId = item.Id,
            Template = item,
            VersionNumber = item.VersionNumber,
            ChangeType = changeType,
            Name = item.Name,
            Category = item.Category,
            Subject = item.Subject,
            BodyHtml = item.BodyHtml,
            IsActive = item.IsActive,
            IsShared = item.IsShared,
            ChangedBy = _accessScope.CurrentUser?.Username ?? string.Empty,
            CreatedAt = createdAt
        };
        private static bool HasChanges(EmailTemplate item, string name, string category, string subject,
            string bodyHtml, bool isActive, bool isShared) =>
            item.Name != name || item.Category != category || item.Subject != subject || item.BodyHtml != bodyHtml ||
            item.IsActive != isActive || item.IsShared != isShared;
        private static string Required(string value, string field) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field}不能为空。") : value.Trim();
        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}

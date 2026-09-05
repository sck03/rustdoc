using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
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

        private static readonly Regex TokenPattern =
            new(@"\{\{[A-Za-z][A-Za-z0-9]*\}\}", RegexOptions.Compiled);

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;
        private readonly IBusinessClock _clock;

        public EmailTemplateService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<IReadOnlyList<EmailTemplateRecord>> ListAsync(
            string? keyword,
            string? category,
            bool includeArchived,
            CancellationToken cancellationToken = default)
        {
            keyword = Clean(keyword);
            category = Clean(category);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplyEmailTemplateScope(context.EmailTemplates.AsNoTracking());
            if (keyword.Length > 0)
            {
                query = query.Where(item =>
                    item.Name.Contains(keyword) ||
                    item.Subject.Contains(keyword) ||
                    item.BodyHtml.Contains(keyword));
            }
            if (category.Length > 0)
            {
                query = query.Where(item => item.Category == category);
            }
            if (!includeArchived)
            {
                query = query.Where(item => item.Status != TemplateLifecycleStatusCatalog.Archived);
            }

            var rows = await query
                .OrderBy(item => item.Category)
                .ThenBy(item => item.Name)
                .ToListAsync(cancellationToken);
            return rows.Select(ToRecord).ToArray();
        }

        public Task<EmailTemplateRecord> SaveDraftAsync(
            EmailTemplateDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string name = Required(request.Name, "模板名称");
            string category = string.IsNullOrWhiteSpace(request.Category) ? "通用" : request.Category.Trim();
            string subject = Clean(request.Subject);
            string rawBodyHtml = Clean(request.BodyHtml);
            if (name.Length > 150 || category.Length > 50 || subject.Length > 300 || rawBodyHtml.Length > 10000)
            {
                throw new ServiceValidationException("邮件模板字段超过允许长度。");
            }

            string bodyHtml = EmailTemplateHtmlPolicy.Sanitize(rawBodyHtml);
            if (bodyHtml.Length > 10000)
            {
                throw new ServiceValidationException("邮件模板正文超过允许长度。");
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    bool isNew = request.Id <= 0;
                    EmailTemplate entity;
                    if (isNew)
                    {
                        _accessScope.DemandPermission(
                            PermissionResourceCatalog.EmailTemplates,
                            PermissionAction.Edit);
                        entity = new EmailTemplate
                        {
                            Status = TemplateLifecycleStatusCatalog.Draft,
                            ShareScope = TemplateShareScopeCatalog.Private,
                            VersionNumber = 1
                        };
                        _accessScope.ApplyOwner(entity);
                        await context.EmailTemplates.AddAsync(entity, token);
                    }
                    else
                    {
                        entity = await context.EmailTemplates
                            .FirstOrDefaultAsync(item => item.Id == request.Id, token)
                            ?? throw new ResourceNotFoundException("邮件模板不存在或无权访问。");
                        if (!_accessScope.IsOwnedByCurrentUser(entity.OwnerUserId) ||
                            !_accessScope.CanAccessOwnedBusinessRecord(
                                entity.OwnerUserId,
                                entity.DepartmentId,
                                entity.CompanyScope,
                                PermissionResourceCatalog.EmailTemplates,
                                PermissionAction.Edit))
                        {
                            throw new ResourceNotFoundException("邮件模板不存在或无权修改。");
                        }
                        if (entity.Status == TemplateLifecycleStatusCatalog.Archived)
                        {
                            throw new ResourceConflictException("归档模板必须先恢复，不能直接修改正文。");
                        }
                        PrepareExpectedVersion(context, entity, request.ExpectedVersion);
                    }

                    bool duplicate = await context.EmailTemplates.AsNoTracking()
                        .AnyAsync(item =>
                            item.Id != entity.Id &&
                            item.OwnerUserId == entity.OwnerUserId &&
                            item.Name == name &&
                            item.Category == category &&
                            item.Status != TemplateLifecycleStatusCatalog.Archived,
                            token);
                    if (duplicate)
                    {
                        throw new ResourceConflictException("同一分类下已存在同名邮件模板。");
                    }

                    bool changed = isNew ||
                                   entity.Name != name ||
                                   entity.Category != category ||
                                   entity.Subject != subject ||
                                   entity.BodyHtml != bodyHtml ||
                                   entity.Status != TemplateLifecycleStatusCatalog.Draft ||
                                   entity.ShareScope != TemplateShareScopeCatalog.Private;
                    if (!changed)
                    {
                        return ToRecord(entity);
                    }

                    entity.Name = name;
                    entity.Category = category;
                    entity.Subject = subject;
                    entity.BodyHtml = bodyHtml;
                    entity.Status = TemplateLifecycleStatusCatalog.Draft;
                    entity.ShareScope = TemplateShareScopeCatalog.Private;
                    AdvanceVersion(entity, isNew);
                    await AddVersionAsync(context, entity, isNew ? "创建草稿" : "保存草稿", token);
                    await SaveChangesAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public Task<EmailTemplateRecord> PublishAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            MutateLifecycleAsync(
                id,
                expectedVersion,
                PermissionAction.Publish,
                "发布",
                entity =>
                {
                    if (entity.Status != TemplateLifecycleStatusCatalog.Draft)
                    {
                        throw new ResourceConflictException("只有草稿邮件模板可以发布。");
                    }
                    entity.Status = TemplateLifecycleStatusCatalog.Published;
                },
                cancellationToken);

        public Task<EmailTemplateRecord> ShareAsync(
            int id,
            EmailTemplateShareRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string shareScope = TemplateShareScopeCatalog.Normalize(request.ShareScope);
            if (shareScope.Length == 0)
            {
                throw new ServiceValidationException("邮件模板共享范围无效。");
            }

            return MutateLifecycleAsync(
                id,
                request.ExpectedVersion,
                PermissionAction.Share,
                "调整共享范围",
                entity =>
                {
                    if (entity.Status is not (TemplateLifecycleStatusCatalog.Published or TemplateLifecycleStatusCatalog.Disabled))
                    {
                        throw new ResourceConflictException("邮件模板发布后才能设置共享范围。");
                    }
                    EnsureShareScopeAvailable(shareScope);
                    entity.ShareScope = shareScope;
                },
                cancellationToken);
        }

        public Task<EmailTemplateRecord> DisableAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            MutateLifecycleAsync(
                id,
                expectedVersion,
                PermissionAction.Deactivate,
                "停用",
                entity =>
                {
                    if (entity.Status != TemplateLifecycleStatusCatalog.Published)
                    {
                        throw new ResourceConflictException("只有已发布邮件模板可以停用。");
                    }
                    entity.Status = TemplateLifecycleStatusCatalog.Disabled;
                },
                cancellationToken);

        public Task<EmailTemplateRecord> RestoreAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            MutateLifecycleAsync(
                id,
                expectedVersion,
                PermissionAction.Restore,
                "恢复",
                entity =>
                {
                    switch (entity.Status)
                    {
                        case TemplateLifecycleStatusCatalog.Disabled:
                            entity.Status = TemplateLifecycleStatusCatalog.Published;
                            break;
                        case TemplateLifecycleStatusCatalog.Archived:
                            entity.Status = TemplateLifecycleStatusCatalog.Draft;
                            entity.ShareScope = TemplateShareScopeCatalog.Private;
                            break;
                        default:
                            throw new ResourceConflictException("当前邮件模板状态不需要恢复。");
                    }
                },
                cancellationToken);

        public Task<EmailTemplateRecord> ArchiveAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            MutateLifecycleAsync(
                id,
                expectedVersion,
                PermissionAction.Delete,
                "归档",
                entity =>
                {
                    if (entity.Status == TemplateLifecycleStatusCatalog.Archived)
                    {
                        throw new ResourceConflictException("邮件模板已经归档。");
                    }
                    entity.Status = TemplateLifecycleStatusCatalog.Archived;
                },
                cancellationToken);

        public async Task<IReadOnlyList<EmailTemplateVersionRecord>> ListVersionsAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var template = await _accessScope.ApplyEmailTemplateScope(context.EmailTemplates.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (template == null)
            {
                return [];
            }

            bool canRestore = CanAct(template, PermissionAction.Restore);
            return await context.EmailTemplateVersions.AsNoTracking()
                .Where(item => item.EmailTemplateId == id)
                .OrderByDescending(item => item.VersionNumber)
                .Select(item => new EmailTemplateVersionRecord(
                    item.Id,
                    item.EmailTemplateId,
                    item.VersionNumber,
                    item.ChangeType,
                    item.Name,
                    item.Category,
                    item.Subject,
                    item.BodyHtml,
                    item.Status,
                    item.ShareScope,
                    item.ChangedBy,
                    item.CreatedAt,
                    canRestore))
                .ToListAsync(cancellationToken);
        }

        public Task<EmailTemplateRecord> RestoreVersionAsync(
            int id,
            int versionNumber,
            int expectedVersion,
            CancellationToken cancellationToken = default)
        {
            if (versionNumber <= 0)
            {
                throw new ServiceValidationException("邮件模板历史版本无效。");
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await LoadForActionAsync(context, id, PermissionAction.Restore, token);
                    PrepareExpectedVersion(context, entity, expectedVersion);
                    var source = await context.EmailTemplateVersions.AsNoTracking()
                        .FirstOrDefaultAsync(item =>
                            item.EmailTemplateId == id && item.VersionNumber == versionNumber,
                            token)
                        ?? throw new ResourceNotFoundException("邮件模板历史版本不存在。");
                    if (source.VersionNumber == entity.VersionNumber)
                    {
                        return ToRecord(entity);
                    }

                    bool duplicate = await context.EmailTemplates.AsNoTracking()
                        .AnyAsync(item =>
                            item.Id != id &&
                            item.OwnerUserId == entity.OwnerUserId &&
                            item.Name == source.Name &&
                            item.Category == source.Category &&
                            item.Status != TemplateLifecycleStatusCatalog.Archived,
                            token);
                    if (duplicate)
                    {
                        throw new ResourceConflictException("恢复后的分类和名称与现有邮件模板重复。");
                    }

                    entity.Name = source.Name;
                    entity.Category = source.Category;
                    entity.Subject = source.Subject;
                    entity.BodyHtml = EmailTemplateHtmlPolicy.Sanitize(source.BodyHtml);
                    entity.Status = TemplateLifecycleStatusCatalog.Draft;
                    entity.ShareScope = TemplateShareScopeCatalog.Private;
                    AdvanceVersion(entity, isNew: false);
                    await AddVersionAsync(context, entity, $"恢复内容 V{source.VersionNumber}", token);
                    await SaveChangesAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public IReadOnlyList<EmailTemplateVariableRecord> ListVariables() => Variables;

        public EmailTemplatePreview Preview(EmailTemplatePreviewRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var supplied = request.Variables ?? new Dictionary<string, string>();
            string subject = request.Subject ?? string.Empty;
            string body = EmailTemplateHtmlPolicy.Sanitize(request.BodyHtml);
            foreach (var variable in Variables)
            {
                supplied.TryGetValue(variable.Key, out string? value);
                value ??= string.Empty;
                subject = subject.Replace(variable.Token, value, StringComparison.Ordinal);
                body = body.Replace(variable.Token, WebUtility.HtmlEncode(value), StringComparison.Ordinal);
            }

            var unresolved = TokenPattern.Matches(subject + "\n" + body)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value)
                .ToArray();
            return new EmailTemplatePreview(subject, body, unresolved);
        }

        private Task<EmailTemplateRecord> MutateLifecycleAsync(
            int id,
            int expectedVersion,
            string action,
            string changeType,
            Action<EmailTemplate> mutate,
            CancellationToken cancellationToken) =>
            AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await LoadForActionAsync(context, id, action, token);
                    PrepareExpectedVersion(context, entity, expectedVersion);
                    string previousStatus = entity.Status;
                    string previousShareScope = entity.ShareScope;
                    mutate(entity);
                    if (entity.Status == previousStatus && entity.ShareScope == previousShareScope)
                    {
                        return ToRecord(entity);
                    }

                    AdvanceVersion(entity, isNew: false);
                    await AddVersionAsync(context, entity, changeType, token);
                    await SaveChangesAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        private async Task<EmailTemplate> LoadForActionAsync(
            AppDbContext context,
            int id,
            string action,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                throw new ServiceValidationException("邮件模板 ID 无效。");
            }

            var entity = await context.EmailTemplates
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("邮件模板不存在或无权访问。");
            if (!CanAct(entity, action))
            {
                throw new ResourceNotFoundException("邮件模板不存在或无权执行该操作。");
            }
            return entity;
        }

        private bool CanAct(EmailTemplate entity, string action) =>
            _accessScope.CanAccessOwnedBusinessRecord(
                entity.OwnerUserId,
                entity.DepartmentId,
                entity.CompanyScope,
                PermissionResourceCatalog.EmailTemplates,
                action);

        private EmailTemplateRecord ToRecord(EmailTemplate item)
        {
            bool canEdit = item.Status != TemplateLifecycleStatusCatalog.Archived &&
                           _accessScope.IsOwnedByCurrentUser(item.OwnerUserId) &&
                           CanAct(item, PermissionAction.Edit);
            return new EmailTemplateRecord(
                item.Id,
                item.Name,
                item.Category,
                item.Subject,
                item.BodyHtml,
                item.Status,
                item.ShareScope,
                item.VersionNumber,
                item.OwnerUserId,
                canEdit,
                item.Status == TemplateLifecycleStatusCatalog.Draft && CanAct(item, PermissionAction.Publish),
                item.Status is TemplateLifecycleStatusCatalog.Published or TemplateLifecycleStatusCatalog.Disabled &&
                    CanAct(item, PermissionAction.Share),
                item.Status == TemplateLifecycleStatusCatalog.Published && CanAct(item, PermissionAction.Deactivate),
                item.Status is TemplateLifecycleStatusCatalog.Disabled or TemplateLifecycleStatusCatalog.Archived &&
                    CanAct(item, PermissionAction.Restore),
                item.Status != TemplateLifecycleStatusCatalog.Archived && CanAct(item, PermissionAction.Delete));
        }

        private async Task AddVersionAsync(
            AppDbContext context,
            EmailTemplate item,
            string changeType,
            CancellationToken cancellationToken)
        {
            item.UpdatedAt = _clock.UtcNow;
            await context.EmailTemplateVersions.AddAsync(new EmailTemplateVersion
            {
                EmailTemplateId = item.Id,
                Template = item,
                VersionNumber = item.VersionNumber,
                ChangeType = changeType,
                Name = item.Name,
                Category = item.Category,
                Subject = item.Subject,
                BodyHtml = item.BodyHtml,
                Status = item.Status,
                ShareScope = item.ShareScope,
                ChangedBy = _accessScope.CurrentUser?.Username ?? string.Empty,
                CreatedAt = item.UpdatedAt
            }, cancellationToken);
        }

        private void EnsureShareScopeAvailable(string shareScope)
        {
            if (shareScope == TemplateShareScopeCatalog.Company &&
                string.IsNullOrWhiteSpace(_accessScope.CurrentUser?.CompanyScope))
            {
                throw new ServiceValidationException("当前账号未归属公司，不能设置公司共享。");
            }
            if (shareScope == TemplateShareScopeCatalog.Department &&
                (string.IsNullOrWhiteSpace(_accessScope.CurrentUser?.CompanyScope) ||
                 string.IsNullOrWhiteSpace(_accessScope.CurrentUser?.DepartmentId)))
            {
                throw new ServiceValidationException("当前账号未归属公司和部门，不能设置部门共享。");
            }
        }

        private static void PrepareExpectedVersion(AppDbContext context, EmailTemplate entity, int expectedVersion)
        {
            if (expectedVersion <= 0)
            {
                throw new BusinessConcurrencyException("操作现有邮件模板时必须提供版本号，请刷新后重试。");
            }
            if (entity.VersionNumber != expectedVersion)
            {
                throw new BusinessConcurrencyException("该邮件模板已被其他用户修改，请刷新后重试。");
            }
            context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
        }

        private static void AdvanceVersion(EmailTemplate entity, bool isNew)
        {
            entity.VersionNumber = isNew ? 1 : checked(entity.VersionNumber + 1);
        }

        private static async Task SaveChangesAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException(
                    "该邮件模板已被其他用户修改，请刷新后重试。",
                    exception);
            }
            catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                throw new ResourceConflictException("邮件模板版本或名称发生并发冲突，请刷新后重试。", exception);
            }
        }

        private static string Required(string? value, string field) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ServiceValidationException($"{field}不能为空。")
                : value.Trim();

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}

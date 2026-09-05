using System.Data;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting
{
    /// <summary>
    /// Owns the multi-user user-template aggregate. Content edits, lifecycle
    /// commands, immutable versions and image references are committed in one
    /// database transaction; built-in file templates remain outside this store.
    /// </summary>
    public sealed class UserReportTemplateService : IUserReportTemplateService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;
        private readonly IBusinessClock _clock;

        public UserReportTemplateService(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<IReadOnlyList<UserReportTemplateRecord>> ListAsync(
            ReportDocumentType reportType,
            bool includeArchived = false,
            CancellationToken cancellationToken = default)
        {
            DemandReportTypeAccess(reportType);
            string type = reportType.ToString();
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope
                .ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                .Where(item => item.ReportType == type);
            if (!includeArchived)
            {
                query = query.Where(item => item.Status != TemplateLifecycleStatusCatalog.Archived);
            }

            var rows = await query
                .OrderByDescending(item => item.Status == TemplateLifecycleStatusCatalog.Published)
                .ThenBy(item => item.Name)
                .ToListAsync(cancellationToken);
            return rows.Select(ToRecord).ToArray();
        }

        public Task<UserReportTemplateRecord> SaveDraftAsync(
            UserReportTemplateDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Enum.TryParse(request.ReportType, true, out ReportDocumentType reportType))
            {
                throw new ServiceValidationException("报表类型无效。");
            }
            DemandReportTypeAccess(reportType);

            string name = Required(request.Name, "模板名称");
            string content = request.ContentHtml ?? string.Empty;
            bool isNew = request.Id <= 0;
            if (string.IsNullOrWhiteSpace(content))
            {
                if (!isNew)
                {
                    throw new ServiceValidationException("报表模板内容不能为空。");
                }
                content = ReportTemplateStarterFactory.Create(reportType, name);
            }
            if (name.Length > 150 || content.Length > 2_000_000)
            {
                throw new ServiceValidationException("报表模板名称或内容超过允许长度。");
            }

            ReportTemplateContentPolicy.Validate(reportType, content);
            IReadOnlyList<ReportTemplateV3ResourceReference> resources =
                ReportTemplateV3ResourceReferenceParser.Parse(reportType, content);

            if (isNew)
            {
                return AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    (context, token) => CreateDraftAsync(
                        context,
                        reportType,
                        name,
                        content,
                        resources,
                        PermissionAction.Design,
                        "创建草稿",
                        token),
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await context.UserReportTemplates
                        .FirstOrDefaultAsync(item => item.Id == request.Id, token)
                        ?? throw new ResourceNotFoundException("报表模板不存在或无权访问。");
                    if (!_accessScope.IsOwnedByCurrentUser(entity.OwnerUserId) ||
                        !CanAct(entity, PermissionAction.Design))
                    {
                        throw new ResourceNotFoundException("报表模板不存在或无权修改。");
                    }
                    if (entity.Status == TemplateLifecycleStatusCatalog.Archived)
                    {
                        throw new ResourceConflictException("归档模板必须先恢复，不能直接修改内容。");
                    }
                    if (!string.Equals(entity.ReportType, reportType.ToString(), StringComparison.Ordinal))
                    {
                        throw new ServiceValidationException("不能修改报表模板类型。");
                    }
                    PrepareExpectedVersion(context, entity, request.ExpectedVersion);

                    bool duplicate = await context.UserReportTemplates.AsNoTracking()
                        .AnyAsync(item =>
                            item.Id != entity.Id &&
                            item.OwnerUserId == entity.OwnerUserId &&
                            item.ReportType == reportType.ToString() &&
                            item.Name == name &&
                            item.Status != TemplateLifecycleStatusCatalog.Archived,
                            token);
                    if (duplicate)
                    {
                        throw new ResourceConflictException("你已经拥有同名报表模板。");
                    }

                    await ValidateResourceAccessAsync(context, entity.Id, resources, token);
                    bool changed = entity.Name != name ||
                                   entity.ContentHtml != content ||
                                   entity.Status != TemplateLifecycleStatusCatalog.Draft ||
                                   entity.ShareScope != TemplateShareScopeCatalog.Private;
                    if (!changed)
                    {
                        return ToRecord(entity);
                    }

                    entity.Name = name;
                    entity.ContentHtml = content;
                    entity.Status = TemplateLifecycleStatusCatalog.Draft;
                    entity.ShareScope = TemplateShareScopeCatalog.Private;
                    AdvanceVersion(entity, isNew: false);
                    await SyncCurrentResourceReferencesAsync(
                        context,
                        entity,
                        resources,
                        ReportTemplateResourceReferenceKind.Draft,
                        token);
                    await AddVersionAsync(context, entity, "保存草稿", resources, token);
                    await SaveChangesAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public Task<UserReportTemplateRecord> CloneAsync(
            UserReportTemplateCloneRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Enum.TryParse(request.ReportType, true, out ReportDocumentType reportType))
            {
                throw new ServiceValidationException("报表类型无效。");
            }
            DemandReportTypeAccess(reportType);

            string name = Required(request.Name, "模板名称");
            if (name.Length > 150)
            {
                throw new ServiceValidationException("报表模板名称超过允许长度。");
            }

            bool hasUserTemplateSource = request.SourceUserTemplateId > 0;
            bool hasBuiltInSource = !string.IsNullOrWhiteSpace(request.ServerResolvedContentHtml);
            if (hasUserTemplateSource == hasBuiltInSource)
            {
                throw new ServiceValidationException("复制报表模板时必须且只能指定一个有效来源。");
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    _accessScope.DemandPermission(
                        PermissionResourceCatalog.ReportTemplates,
                        PermissionAction.Clone);

                    string content;
                    if (hasUserTemplateSource)
                    {
                        var source = await _accessScope
                            .ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                            .Where(item =>
                                item.Id == request.SourceUserTemplateId &&
                                item.ReportType == reportType.ToString())
                            .Select(item => new { item.ContentHtml })
                            .FirstOrDefaultAsync(token)
                            ?? throw new ResourceNotFoundException("复制来源模板不存在或无权访问。");
                        content = source.ContentHtml ?? string.Empty;
                    }
                    else
                    {
                        content = request.ServerResolvedContentHtml;
                    }

                    if (string.IsNullOrWhiteSpace(content) || content.Length > 2_000_000)
                    {
                        throw new ServiceValidationException("复制来源模板内容为空或超过允许长度。");
                    }

                    ReportTemplateContentPolicy.Validate(reportType, content);
                    IReadOnlyList<ReportTemplateV3ResourceReference> resources =
                        ReportTemplateV3ResourceReferenceParser.Parse(reportType, content);
                    return await CreateDraftAsync(
                        context,
                        reportType,
                        name,
                        content,
                        resources,
                        PermissionAction.Clone,
                        "复制草稿",
                        token);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public Task<UserReportTemplateRecord> PublishAsync(
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
                        throw new ResourceConflictException("只有草稿报表模板可以发布。");
                    }
                    entity.Status = TemplateLifecycleStatusCatalog.Published;
                },
                cancellationToken);

        public Task<UserReportTemplateRecord> ShareAsync(
            int id,
            UserReportTemplateShareRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string shareScope = TemplateShareScopeCatalog.Normalize(request.ShareScope);
            if (shareScope.Length == 0)
            {
                throw new ServiceValidationException("报表模板共享范围无效。");
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
                        throw new ResourceConflictException("报表模板发布后才能设置共享范围。");
                    }
                    EnsureShareScopeAvailable(shareScope);
                    entity.ShareScope = shareScope;
                },
                cancellationToken);
        }

        public Task<UserReportTemplateRecord> DisableAsync(
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
                        throw new ResourceConflictException("只有已发布报表模板可以停用。");
                    }
                    entity.Status = TemplateLifecycleStatusCatalog.Disabled;
                },
                cancellationToken);

        public Task<UserReportTemplateRecord> RestoreAsync(
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
                            throw new ResourceConflictException("当前报表模板状态不需要恢复。");
                    }
                },
                cancellationToken);

        public Task<UserReportTemplateRecord> ArchiveAsync(
            int id,
            int expectedVersion,
            CancellationToken cancellationToken = default) =>
            MutateLifecycleAsync(
                id,
                expectedVersion,
                PermissionAction.Archive,
                "归档",
                entity =>
                {
                    if (entity.Status == TemplateLifecycleStatusCatalog.Archived)
                    {
                        throw new ResourceConflictException("报表模板已经归档。");
                    }
                    entity.Status = TemplateLifecycleStatusCatalog.Archived;
                    entity.ShareScope = TemplateShareScopeCatalog.Private;
                },
                cancellationToken);

        public async Task<IReadOnlyList<UserReportTemplateVersionRecord>> ListVersionsAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var template = await _accessScope
                .ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (template == null)
            {
                return [];
            }
            DemandReportTypeAccess(Enum.Parse<ReportDocumentType>(template.ReportType, true));

            bool canRestore = CanAct(template, PermissionAction.Restore);
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
                    item.Status,
                    item.ShareScope,
                    item.ChangedBy,
                    item.CreatedAt,
                    canRestore))
                .ToListAsync(cancellationToken);
        }

        public Task<UserReportTemplateRecord> RestoreVersionAsync(
            int id,
            int versionNumber,
            int expectedVersion,
            CancellationToken cancellationToken = default)
        {
            if (versionNumber <= 0)
            {
                throw new ServiceValidationException("报表模板历史版本无效。");
            }

            return AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var entity = await LoadForActionAsync(context, id, PermissionAction.Restore, token);
                    PrepareExpectedVersion(context, entity, expectedVersion);
                    var source = await context.UserReportTemplateVersions.AsNoTracking()
                        .FirstOrDefaultAsync(item =>
                            item.UserReportTemplateId == id && item.VersionNumber == versionNumber,
                            token)
                        ?? throw new ResourceNotFoundException("报表模板历史版本不存在。");
                    if (source.VersionNumber == entity.VersionNumber)
                    {
                        return ToRecord(entity);
                    }

                    ReportDocumentType reportType = Enum.Parse<ReportDocumentType>(entity.ReportType, true);
                    ReportTemplateContentPolicy.Validate(reportType, source.ContentHtml);
                    IReadOnlyList<ReportTemplateV3ResourceReference> resources =
                        ReportTemplateV3ResourceReferenceParser.Parse(reportType, source.ContentHtml);
                    await ValidateResourceAccessAsync(context, entity.Id, resources, token);

                    bool duplicate = await context.UserReportTemplates.AsNoTracking()
                        .AnyAsync(item =>
                            item.Id != id &&
                            item.OwnerUserId == entity.OwnerUserId &&
                            item.ReportType == entity.ReportType &&
                            item.Name == source.Name &&
                            item.Status != TemplateLifecycleStatusCatalog.Archived,
                            token);
                    if (duplicate)
                    {
                        throw new ResourceConflictException("恢复后的名称与现有报表模板重复。");
                    }

                    entity.Name = source.Name;
                    entity.ContentHtml = source.ContentHtml;
                    entity.Status = TemplateLifecycleStatusCatalog.Draft;
                    entity.ShareScope = TemplateShareScopeCatalog.Private;
                    AdvanceVersion(entity, isNew: false);
                    await SyncCurrentResourceReferencesAsync(
                        context,
                        entity,
                        resources,
                        ReportTemplateResourceReferenceKind.Draft,
                        token);
                    await AddVersionAsync(context, entity, $"恢复内容 V{source.VersionNumber}", resources, token);
                    await SaveChangesAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private async Task<UserReportTemplateRecord> CreateDraftAsync(
            AppDbContext context,
            ReportDocumentType reportType,
            string name,
            string content,
            IReadOnlyList<ReportTemplateV3ResourceReference> resources,
            string permissionAction,
            string changeType,
            CancellationToken cancellationToken)
        {
            _accessScope.DemandPermission(
                PermissionResourceCatalog.ReportTemplates,
                permissionAction);

            var entity = new UserReportTemplate
            {
                ReportType = reportType.ToString(),
                Name = name,
                ContentHtml = content,
                Status = TemplateLifecycleStatusCatalog.Draft,
                ShareScope = TemplateShareScopeCatalog.Private,
                VersionNumber = 1
            };
            _accessScope.ApplyOwner(entity);

            bool duplicate = await context.UserReportTemplates.AsNoTracking()
                .AnyAsync(item =>
                    item.OwnerUserId == entity.OwnerUserId &&
                    item.ReportType == entity.ReportType &&
                    item.Name == name &&
                    item.Status != TemplateLifecycleStatusCatalog.Archived,
                    cancellationToken);
            if (duplicate)
            {
                throw new ResourceConflictException("你已经拥有同名报表模板。");
            }

            await ValidateResourceAccessAsync(context, 0, resources, cancellationToken);
            await context.UserReportTemplates.AddAsync(entity, cancellationToken);
            await SyncCurrentResourceReferencesAsync(
                context,
                entity,
                resources,
                ReportTemplateResourceReferenceKind.Draft,
                cancellationToken);
            await AddVersionAsync(context, entity, changeType, resources, cancellationToken);
            await SaveChangesAsync(context, cancellationToken);
            return ToRecord(entity);
        }

        private Task<UserReportTemplateRecord> MutateLifecycleAsync(
            int id,
            int expectedVersion,
            string action,
            string changeType,
            Action<UserReportTemplate> mutate,
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

                    ReportDocumentType reportType = Enum.Parse<ReportDocumentType>(entity.ReportType, true);
                    IReadOnlyList<ReportTemplateV3ResourceReference> resources =
                        ReportTemplateV3ResourceReferenceParser.Parse(reportType, entity.ContentHtml);
                    await ValidateResourceAccessAsync(context, entity.Id, resources, token);
                    AdvanceVersion(entity, isNew: false);
                    string referenceKind = entity.Status is TemplateLifecycleStatusCatalog.Published or TemplateLifecycleStatusCatalog.Disabled
                        ? ReportTemplateResourceReferenceKind.Published
                        : ReportTemplateResourceReferenceKind.Draft;
                    await SyncCurrentResourceReferencesAsync(context, entity, resources, referenceKind, token);
                    await AddVersionAsync(context, entity, changeType, resources, token);
                    await SaveChangesAsync(context, token);
                    return ToRecord(entity);
                },
                IsolationLevel.Serializable,
                cancellationToken);

        private async Task<UserReportTemplate> LoadForActionAsync(
            AppDbContext context,
            int id,
            string action,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                throw new ServiceValidationException("报表模板 ID 无效。");
            }

            var entity = await context.UserReportTemplates
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new ResourceNotFoundException("报表模板不存在或无权访问。");
            DemandReportTypeAccess(Enum.Parse<ReportDocumentType>(entity.ReportType, true));
            if (!CanAct(entity, action))
            {
                throw new ResourceNotFoundException("报表模板不存在或无权执行该操作。");
            }
            return entity;
        }

        private void DemandReportTypeAccess(ReportDocumentType reportType) =>
            _accessScope.DemandPermission(
                ReportDocumentAccessCatalog.GetSourceResource(reportType),
                PermissionAction.View);

        private bool CanAct(UserReportTemplate entity, string action) =>
            _accessScope.CanAccessOwnedBusinessRecord(
                entity.OwnerUserId,
                entity.DepartmentId,
                entity.CompanyScope,
                PermissionResourceCatalog.ReportTemplates,
                action);

        private UserReportTemplateRecord ToRecord(UserReportTemplate item)
        {
            bool canEdit = item.Status != TemplateLifecycleStatusCatalog.Archived &&
                           _accessScope.IsOwnedByCurrentUser(item.OwnerUserId) &&
                           CanAct(item, PermissionAction.Design);
            return new UserReportTemplateRecord(
                item.Id,
                item.ReportType,
                item.Name,
                item.ContentHtml,
                item.Status,
                item.ShareScope,
                item.VersionNumber,
                canEdit,
                item.Status == TemplateLifecycleStatusCatalog.Draft && CanAct(item, PermissionAction.Publish),
                item.Status is TemplateLifecycleStatusCatalog.Published or TemplateLifecycleStatusCatalog.Disabled &&
                    CanAct(item, PermissionAction.Share),
                item.Status == TemplateLifecycleStatusCatalog.Published && CanAct(item, PermissionAction.Deactivate),
                item.Status is TemplateLifecycleStatusCatalog.Disabled or TemplateLifecycleStatusCatalog.Archived &&
                    CanAct(item, PermissionAction.Restore),
                item.Status != TemplateLifecycleStatusCatalog.Archived && CanAct(item, PermissionAction.Archive),
                item.OwnerUserId);
        }

        private async Task ValidateResourceAccessAsync(
            AppDbContext context,
            int templateId,
            IReadOnlyList<ReportTemplateV3ResourceReference> resources,
            CancellationToken cancellationToken)
        {
            if (resources.Count == 0)
            {
                return;
            }

            string[] ids = resources.Select(item => item.Id).ToArray();
            var entries = await context.ReportTemplateImageResources.AsNoTracking()
                .Where(item => ids.Contains(item.Id) && item.RecycledAt == null)
                .ToDictionaryAsync(item => item.Id, StringComparer.Ordinal, cancellationToken);
            if (entries.Count != ids.Length)
            {
                throw new ResourceNotFoundException("报表模板引用的图片尚未上传、已回收或无权使用。");
            }

            foreach (var resource in resources)
            {
                ReportTemplateImageResourceEntry entry = entries[resource.Id];
                if (!string.Equals(entry.Sha256, resource.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(entry.MediaType, resource.MediaType, StringComparison.Ordinal) ||
                    entry.ByteLength != resource.ByteLength)
                {
                    throw new ServiceValidationException("报表模板图片清单与已登记资源不一致。");
                }
            }

            if (!_accessScope.UsesPostgreSql)
            {
                return;
            }

            int userId = _accessScope.CurrentUser?.Id ?? 0;
            IQueryable<int> visibleTemplateIds = _accessScope
                .ApplyUserReportTemplateScope(context.UserReportTemplates.AsNoTracking())
                .Select(item => item.Id);
            var allowedIds = await context.ReportTemplateImageResources.AsNoTracking()
                .Where(entry => ids.Contains(entry.Id) &&
                    (context.ReportTemplateImageResourceUploadClaims.Any(claim =>
                         claim.ResourceId == entry.Id && claim.UserId == userId) ||
                     context.UserReportTemplateResourceReferences.Any(reference =>
                         reference.ResourceId == entry.Id &&
                         (reference.UserReportTemplateId == templateId ||
                          visibleTemplateIds.Contains(reference.UserReportTemplateId)))))
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            if (allowedIds.Count != ids.Length)
            {
                throw new ResourceNotFoundException("报表模板引用的图片尚未上传、已回收或无权使用。");
            }
        }

        private async Task SyncCurrentResourceReferencesAsync(
            AppDbContext context,
            UserReportTemplate template,
            IReadOnlyList<ReportTemplateV3ResourceReference> resources,
            string referenceKind,
            CancellationToken cancellationToken)
        {
            if (template.Id > 0)
            {
                var existing = await context.UserReportTemplateResourceReferences
                    .Where(item => item.UserReportTemplateId == template.Id)
                    .ToListAsync(cancellationToken);
                context.UserReportTemplateResourceReferences.RemoveRange(existing);
            }

            foreach (var resource in resources)
            {
                await context.UserReportTemplateResourceReferences.AddAsync(
                    new UserReportTemplateResourceReference
                    {
                        UserReportTemplateId = template.Id,
                        Template = template,
                        ResourceId = resource.Id,
                        ReferenceKind = referenceKind,
                        CreatedAt = _clock.UtcNow
                    },
                    cancellationToken);
            }
        }

        private async Task AddVersionAsync(
            AppDbContext context,
            UserReportTemplate template,
            string changeType,
            IReadOnlyList<ReportTemplateV3ResourceReference> resources,
            CancellationToken cancellationToken)
        {
            template.UpdatedAt = _clock.UtcNow;
            var version = new UserReportTemplateVersion
            {
                UserReportTemplateId = template.Id,
                Template = template,
                VersionNumber = template.VersionNumber,
                ChangeType = changeType,
                Name = template.Name,
                ContentHtml = template.ContentHtml,
                Status = template.Status,
                ShareScope = template.ShareScope,
                ChangedBy = _accessScope.CurrentUser?.Username ?? string.Empty,
                CreatedAt = template.UpdatedAt
            };
            await context.UserReportTemplateVersions.AddAsync(version, cancellationToken);
            foreach (var resource in resources)
            {
                await context.UserReportTemplateVersionResourceReferences.AddAsync(
                    new UserReportTemplateVersionResourceReference
                    {
                        UserReportTemplateVersionId = version.Id,
                        Version = version,
                        ResourceId = resource.Id,
                        CreatedAt = template.UpdatedAt
                    },
                    cancellationToken);
            }
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

        private static void PrepareExpectedVersion(
            AppDbContext context,
            UserReportTemplate entity,
            int expectedVersion)
        {
            if (expectedVersion <= 0)
            {
                throw new UserReportTemplateConcurrencyException("操作现有报表模板时必须提供版本号，请刷新后重试。");
            }
            if (entity.VersionNumber != expectedVersion)
            {
                throw new UserReportTemplateConcurrencyException("报表模板已被其他用户修改，请刷新后重试。");
            }
            context.Entry(entity).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
        }

        private static void AdvanceVersion(UserReportTemplate entity, bool isNew)
        {
            entity.VersionNumber = isNew ? 1 : checked(entity.VersionNumber + 1);
        }

        private static async Task SaveChangesAsync(
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new UserReportTemplateConcurrencyException(
                    "报表模板已被其他用户修改，请刷新后重试。",
                    exception);
            }
            catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                throw new ResourceConflictException("报表模板版本、名称或资源引用发生并发冲突，请刷新后重试。", exception);
            }
        }

        private static string Required(string? value, string field) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ServiceValidationException($"{field}不能为空。")
                : value.Trim();
    }
}

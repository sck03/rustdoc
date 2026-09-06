using System.Data;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Security
{
    public sealed class PermissionTemplateService : IPermissionTemplateService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IBusinessClock _clock;

        public PermissionTemplateService(
            IDbContextFactory<AppDbContext> contextFactory,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<IReadOnlyList<PermissionTemplateRecord>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return (await context.PermissionTemplates
                    .AsNoTracking()
                    .Include(template => template.Grants)
                    .OrderByDescending(template => template.IsSystem)
                    .ThenBy(template => template.Name)
                    .ToListAsync(cancellationToken))
                .Select(ToRecord)
                .ToArray();
        }

        public async Task<PermissionTemplateRecord> SaveAsync(
            PermissionTemplateSaveRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string code = NormalizeCode(request.Code);
            string name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ServiceValidationException("权限模板名称不能为空。");
            }

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    string normalizedCode = CanonicalKey(code);
                    bool duplicateCode = await context.PermissionTemplates.AnyAsync(
                        template => template.Id != request.Id && template.CodeNormalized == normalizedCode,
                        token);
                    if (duplicateCode)
                    {
                        throw new ResourceConflictException("权限模板代码已存在。");
                    }

                    PermissionTemplate template;
                    if (request.Id <= 0)
                    {
                        if (request.ExpectedVersion != 0)
                        {
                            throw new BusinessConcurrencyException("新建权限模板不能携带旧版本号，请刷新后重试。");
                        }

                        template = new PermissionTemplate
                        {
                            Code = code,
                            IsSystem = false,
                            VersionNumber = 1
                        };
                        context.PermissionTemplates.Add(template);
                    }
                    else
                    {
                        if (request.ExpectedVersion <= 0)
                        {
                            throw new BusinessConcurrencyException("保存现有权限模板时必须提供版本号，请刷新后重试。");
                        }

                        template = await context.PermissionTemplates
                            .Include(item => item.Grants)
                            .FirstOrDefaultAsync(item => item.Id == request.Id, token)
                            ?? throw new ResourceNotFoundException("未找到权限模板。");
                        if (template.VersionNumber != request.ExpectedVersion)
                        {
                            throw new BusinessConcurrencyException("该权限模板已被其他管理员修改，请刷新后重试。");
                        }

                        if (template.IsSystem &&
                            string.Equals(template.Code, BuiltInPermissionTemplateCatalog.Admin, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ResourceConflictException("系统管理员模板不可修改。");
                        }

                        context.Entry(template).Property(item => item.VersionNumber).OriginalValue = request.ExpectedVersion;
                        template.VersionNumber++;
                        context.PermissionTemplateGrants.RemoveRange(template.Grants);
                        template.Grants.Clear();
                        if (!template.IsSystem)
                        {
                            template.Code = code;
                        }
                    }

                    var grants = NormalizeGrants(request.Grants);
                    template.Name = name;
                    template.Description = (request.Description ?? string.Empty).Trim();
                    template.IsActive = template.IsSystem || request.IsActive;
                    template.UpdatedAt = _clock.UtcNow;
                    template.Grants.AddRange(grants.Select(grant => new PermissionTemplateGrant
                    {
                        ResourceKey = grant.ResourceKey,
                        Action = grant.Action,
                        DataScope = grant.DataScope
                    }));

                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new BusinessConcurrencyException("该权限模板已被其他管理员修改，请刷新后重试。", exception);
                    }
                    catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
                    {
                        throw new ResourceConflictException("权限模板代码已存在。", exception);
                    }

                    return ToRecord(template);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public async Task<bool> DeleteAsync(
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0)
        {
            if (id <= 0) return false;
            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var template = await context.PermissionTemplates
                        .FirstOrDefaultAsync(item => item.Id == id, token);
                    if (template == null) return false;
                    if (expectedVersion <= 0 || template.VersionNumber != expectedVersion)
                    {
                        throw new BusinessConcurrencyException("该权限模板已被其他管理员修改，请刷新后重试。");
                    }
                    if (template.IsSystem)
                    {
                        throw new ResourceConflictException("系统内置权限模板不可删除。");
                    }

                    bool inUse = await context.Users.AnyAsync(user => user.PermissionTemplateId == id, token);
                    if (inUse)
                    {
                        throw new ResourceConflictException("权限模板仍有用户使用，不能删除。");
                    }

                    context.Entry(template).Property(item => item.VersionNumber).OriginalValue = expectedVersion;
                    context.PermissionTemplates.Remove(template);
                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new BusinessConcurrencyException("该权限模板已被其他管理员修改，请刷新后重试。", exception);
                    }
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        public async Task<IReadOnlyList<int>> ListAssignedUserIdsAsync(
            int templateId,
            CancellationToken cancellationToken = default)
        {
            if (templateId <= 0)
            {
                return [];
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Users
                .AsNoTracking()
                .Where(user => user.PermissionTemplateId == templateId)
                .Select(user => user.Id)
                .ToArrayAsync(cancellationToken);
        }

        private static PermissionTemplateRecord ToRecord(PermissionTemplate template) =>
            new(
                template.Id,
                template.Code ?? string.Empty,
                template.Name ?? string.Empty,
                template.Description ?? string.Empty,
                template.IsSystem,
                template.IsActive,
                template.UpdatedAt,
                NormalizePersistedGrants(template.Grants),
                PermissionResourceCatalog.ExpandDependencies(NormalizePersistedGrants(template.Grants)),
                template.VersionNumber);

        private static string CanonicalKey(string? value) =>
            (value ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormC).ToUpperInvariant();

        private static string NormalizeCode(string value)
        {
            string code = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ServiceValidationException("权限模板代码不能为空。");
            }

            if (code.Length > 50 || code.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            {
                throw new ServiceValidationException("权限模板代码只能包含字母、数字、点、横线和下划线，且不能超过 50 个字符。");
            }

            return code;
        }

        private static IReadOnlyList<PermissionGrantRecord> NormalizeGrants(
            IReadOnlyList<PermissionGrantRecord> grants)
        {
            var submitted = grants ?? [];
            var invalidGrant = submitted.FirstOrDefault(grant =>
                !PermissionResourceCatalog.IsKnownAction(grant.ResourceKey, grant.Action) ||
                !PermissionDataScope.IsKnown(grant.DataScope));
            if (invalidGrant != null)
            {
                throw new ServiceValidationException(
                    $"权限 {invalidGrant.ResourceKey}/{invalidGrant.Action} 或其数据范围无效，请刷新权限目录后重试。");
            }

            var technicalGrant = submitted.FirstOrDefault(grant =>
                PermissionResourceCatalog.ByKey.TryGetValue(grant.ResourceKey?.Trim() ?? string.Empty, out var resource) &&
                resource.IsTechnical);
            if (technicalGrant != null)
            {
                throw new ServiceValidationException(
                    $"{PermissionResourceCatalog.ByKey[technicalGrant.ResourceKey.Trim()].Name} 是系统身份或技术依赖能力，不能由岗位模板直接授予。");
            }

            var duplicate = submitted
                .GroupBy(grant => PermissionResourceCatalog.CreateGrantKey(grant.ResourceKey, grant.Action),
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                var item = duplicate.First();
                throw new ServiceValidationException($"权限 {item.ResourceKey}/{item.Action} 被重复提交。");
            }

            return submitted
                .Select(PermissionResourceCatalog.NormalizeGrant)
                .OrderBy(grant => PermissionResourceCatalog.ByKey[grant.ResourceKey].SortOrder)
                .ThenBy(grant => PermissionResourceCatalog.ByKey[grant.ResourceKey].Actions.Single(action =>
                    string.Equals(action.Key, grant.Action, StringComparison.OrdinalIgnoreCase)).SortOrder)
                .ToArray();
        }

        private static IReadOnlyList<PermissionGrantRecord> NormalizePersistedGrants(
            IEnumerable<PermissionTemplateGrant> grants)
        {
            return grants
                .Where(grant => PermissionResourceCatalog.IsKnownAction(grant.ResourceKey, grant.Action) &&
                    PermissionDataScope.IsKnown(grant.DataScope))
                .GroupBy(grant => PermissionResourceCatalog.CreateGrantKey(grant.ResourceKey, grant.Action),
                    StringComparer.OrdinalIgnoreCase)
                // Duplicate/corrupt rows fail closed to the narrowest scope.
                .Select(group => new PermissionGrantRecord(
                    PermissionResourceCatalog.ByKey[group.First().ResourceKey].Key,
                    PermissionResourceCatalog.ByKey[group.First().ResourceKey].Actions.Single(action =>
                        string.Equals(action.Key, group.First().Action, StringComparison.OrdinalIgnoreCase)).Key,
                    group.Select(item => PermissionDataScope.Normalize(item.DataScope))
                        .OrderBy(PermissionDataScope.Rank)
                        .First()))
                .OrderBy(grant => PermissionResourceCatalog.ByKey[grant.ResourceKey].SortOrder)
                .ThenBy(grant => PermissionResourceCatalog.ByKey[grant.ResourceKey].Actions.Single(action =>
                    string.Equals(action.Key, grant.Action, StringComparison.OrdinalIgnoreCase)).SortOrder)
                .ToArray();
        }
    }
}

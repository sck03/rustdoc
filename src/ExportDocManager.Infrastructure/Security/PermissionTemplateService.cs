using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Security
{
    public sealed class PermissionTemplateService : IPermissionTemplateService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public PermissionTemplateService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<IReadOnlyList<PermissionTemplateRecord>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return (await context.PermissionTemplates
                    .AsNoTracking()
                    .Include(template => template.Modules)
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
                throw new ArgumentException("权限模板名称不能为空。");
            }

            var modules = NormalizeModules(request.Modules);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            bool duplicateCode = await context.PermissionTemplates.AnyAsync(
                template => template.Id != request.Id && template.Code == code,
                cancellationToken);
            if (duplicateCode)
            {
                throw new InvalidOperationException("权限模板代码已存在。");
            }

            PermissionTemplate template;
            if (request.Id <= 0)
            {
                template = new PermissionTemplate
                {
                    Code = code,
                    IsSystem = false
                };
                context.PermissionTemplates.Add(template);
            }
            else
            {
                template = await context.PermissionTemplates
                    .Include(item => item.Modules)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("未找到权限模板。");
                if (template.IsSystem &&
                    string.Equals(template.Code, BuiltInPermissionTemplateCatalog.Admin, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("系统管理员模板不可修改。");
                }

                context.PermissionTemplateModules.RemoveRange(template.Modules);
                template.Modules.Clear();
                if (!template.IsSystem)
                {
                    template.Code = code;
                }
            }

            template.Name = name;
            template.Description = (request.Description ?? string.Empty).Trim();
            template.IsActive = template.IsSystem || request.IsActive;
            template.UpdatedAt = DateTime.UtcNow;
            template.Modules = modules.Select(module => new PermissionTemplateModule
            {
                ModuleKey = module.ModuleKey,
                AccessLevel = module.AccessLevel
            }).ToList();

            await context.SaveChangesAsync(cancellationToken);
            return ToRecord(template);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0) return false;
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var template = await context.PermissionTemplates
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (template == null) return false;
            if (template.IsSystem)
            {
                throw new InvalidOperationException("系统内置权限模板不可删除。");
            }

            bool inUse = await context.Users.AnyAsync(user => user.PermissionTemplateId == id, cancellationToken);
            if (inUse)
            {
                throw new InvalidOperationException("权限模板仍有用户使用，不能删除。");
            }

            context.PermissionTemplates.Remove(template);
            await context.SaveChangesAsync(cancellationToken);
            return true;
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
                template.Modules
                    .OrderBy(module => PermissionModuleCatalog.ByKey.TryGetValue(module.ModuleKey, out var definition)
                        ? definition.SortOrder
                        : int.MaxValue)
                    .Select(module => new PermissionTemplateModuleRecord(
                        module.ModuleKey,
                        PermissionAccessLevel.Normalize(module.AccessLevel)))
                    .ToArray());

        private static string NormalizeCode(string value)
        {
            string code = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("权限模板代码不能为空。");
            }

            if (code.Length > 50 || code.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            {
                throw new ArgumentException("权限模板代码只能包含字母、数字、点、横线和下划线，且不能超过 50 个字符。");
            }

            return code;
        }

        private static IReadOnlyList<PermissionTemplateModuleRecord> NormalizeModules(
            IReadOnlyList<PermissionTemplateModuleRecord> modules)
        {
            var submitted = modules ?? [];
            var invalidAccessLevel = submitted.FirstOrDefault(module =>
                PermissionModuleCatalog.IsKnown(module.ModuleKey) &&
                !PermissionAccessLevel.IsKnown(module.AccessLevel));
            if (invalidAccessLevel != null)
            {
                throw new ArgumentException($"模块 {invalidAccessLevel.ModuleKey} 的访问级别无效。");
            }

            var normalized = submitted
                .Where(module => PermissionModuleCatalog.IsKnown(module.ModuleKey))
                .GroupBy(module => module.ModuleKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new PermissionTemplateModuleRecord(
                    group.Key,
                    PermissionAccessLevel.Normalize(group.Last().AccessLevel)))
                .ToArray();

            return PermissionModuleCatalog.ExpandDependencies(normalized)
                .Select(grant => new PermissionTemplateModuleRecord(grant.Key, grant.Value))
                .ToArray();
        }
    }
}

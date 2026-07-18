using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Security
{
    public static class UserPermissionAccessResolver
    {
        public static void PopulateEffectiveModuleAccess(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            if (user.PermissionTemplate != null)
            {
                user.EffectiveModuleAccess = user.PermissionTemplate.IsActive
                    ? user.PermissionTemplate.Modules
                        .Where(module => PermissionModuleCatalog.IsKnown(module.ModuleKey))
                        .GroupBy(module => module.ModuleKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => PermissionAccessLevel.Normalize(group.Last().AccessLevel),
                            StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            if (user.PermissionTemplateId == null)
            {
                user.EffectiveModuleAccess = BuiltInPermissionTemplateCatalog.FindForRole(user.Role)
                    .GetModuleAccess();
                return;
            }

            user.EffectiveModuleAccess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

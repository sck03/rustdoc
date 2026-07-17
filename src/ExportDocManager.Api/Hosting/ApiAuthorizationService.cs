using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiAuthorizationService
    {
        private readonly string _productEdition;

        public ApiAuthorizationService(ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            _productEdition = ProductEditionCatalog.Normalize(runtimeOptions.ProductEdition);
        }

        public bool CanManageSettings(User user)
        {
            return IsAdministrator(user);
        }

        public bool CanManageUsers(User user)
        {
            return IsAdministrator(user) &&
                   string.Equals(_productEdition, ProductEditionCatalog.Full, StringComparison.OrdinalIgnoreCase);
        }

        public bool CanManageAuditLogs(User user)
        {
            return IsAdministrator(user) &&
                   string.Equals(_productEdition, ProductEditionCatalog.Full, StringComparison.OrdinalIgnoreCase);
        }

        public bool CanViewAllBusinessData(User user)
        {
            return BusinessDataAccessScope.CanViewAllBusinessData(user);
        }

        public bool CanUseDocumentWorkspace(User user)
        {
            if (!ProductEditionCatalog.IncludesDocumentWorkspace(_productEdition))
            {
                return false;
            }

            return GetEnabledModules(user).Any(moduleKey =>
                PermissionModuleCatalog.ByKey.TryGetValue(moduleKey, out var definition) &&
                definition.Workspace == "document");
        }

        public bool CanUseSalesWorkspace(User user)
        {
            if (!ProductEditionCatalog.IncludesSalesWorkspace(_productEdition))
            {
                return false;
            }

            return GetEnabledModules(user).Any(moduleKey =>
                PermissionModuleCatalog.ByKey.TryGetValue(moduleKey, out var definition) &&
                definition.Workspace == "sales");
        }

        public bool CanUseModule(
            User user,
            string moduleKey,
            string requiredAccessLevel = PermissionAccessLevel.View)
        {
            if (!PermissionModuleCatalog.ByKey.TryGetValue(moduleKey ?? string.Empty, out var definition))
            {
                return false;
            }

            if (definition.Workspace == "document" &&
                !ProductEditionCatalog.IncludesDocumentWorkspace(_productEdition))
            {
                return false;
            }

            if (definition.Workspace == "sales" &&
                !ProductEditionCatalog.IncludesSalesWorkspace(_productEdition))
            {
                return false;
            }

            var moduleAccess = GetModuleAccess(user);
            string grantedAccessLevel = moduleAccess.TryGetValue(definition.Key, out var accessLevel)
                ? accessLevel
                : string.Empty;
            return AccessRank(grantedAccessLevel) >= AccessRank(requiredAccessLevel);
        }

        public IReadOnlyList<string> GetEnabledModules(User user)
        {
            return GetModuleAccess(user).Keys.ToArray();
        }

        public IReadOnlyDictionary<string, string> GetModuleAccess(User user)
        {
            IEnumerable<KeyValuePair<string, string>> grants = IsAdministrator(user)
                ? PermissionModuleCatalog.Modules.Select(module =>
                    new KeyValuePair<string, string>(module.Key, PermissionAccessLevel.Manage))
                : ReadUserModuleAccess(user);

            return grants
                .Where(grant => PermissionModuleCatalog.ByKey.TryGetValue(grant.Key, out var definition) &&
                    (definition.Workspace == "common" ||
                     definition.Workspace == "document" && ProductEditionCatalog.IncludesDocumentWorkspace(_productEdition) ||
                     definition.Workspace == "sales" && ProductEditionCatalog.IncludesSalesWorkspace(_productEdition)))
                .GroupBy(grant => grant.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => PermissionModuleCatalog.ByKey[group.Key].SortOrder)
                .ToDictionary(
                    group => group.Key,
                    group => PermissionAccessLevel.Normalize(group.Last().Value),
                    StringComparer.OrdinalIgnoreCase);
        }

        public ApiUserCapabilitiesDto GetCapabilities(User user)
        {
            var moduleAccess = GetModuleAccess(user);
            var enabledModules = moduleAccess.Keys.ToArray();
            return new ApiUserCapabilitiesDto(
                CanManageSettings(user),
                CanManageUsers(user),
                CanViewAllBusinessData(user),
                CanUseDocumentWorkspace(user),
                CanUseSalesWorkspace(user),
                _productEdition,
                enabledModules,
                moduleAccess.Select(grant => new ApiModuleAccessDto(grant.Key, grant.Value)).ToArray());
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadUserModuleAccess(User user)
        {
            if (user?.EffectiveModuleAccess?.Count > 0)
            {
                return user.EffectiveModuleAccess;
            }

            if (user?.PermissionTemplateId != null)
            {
                return [];
            }

            return BuiltInPermissionTemplateCatalog.FindForRole(user?.Role).GetModuleAccess();
        }

        private static int AccessRank(string accessLevel) =>
            !PermissionAccessLevel.IsKnown(accessLevel)
                ? 0
                : PermissionAccessLevel.Normalize(accessLevel) switch
            {
                PermissionAccessLevel.Manage => 3,
                PermissionAccessLevel.Operate => 2,
                PermissionAccessLevel.View => 1,
                _ => 0
            };

        private static bool IsAdministrator(User user)
        {
            return string.Equals(
                user?.Role?.Trim(),
                UserRoleCatalog.Admin,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}

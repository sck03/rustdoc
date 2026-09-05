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

        public bool CanManageDisasterRecovery(User user)
        {
            // Disaster recovery changes the complete installation and can
            // replace the database and runtime files.  It is an identity
            // capability, never a user-configurable module grant.
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

            return GetPermissionGrants(user).Any(grant =>
                PermissionResourceCatalog.ByKey.TryGetValue(grant.ResourceKey, out var definition) &&
                definition.Workspace == "sales" &&
                // A document or finance role may read shared mail templates while
                // composing a document.  That dependency must not expose the sales
                // workspace by itself; explicit template maintenance still does.
                (grant.ResourceKey != PermissionResourceCatalog.EmailTemplates ||
                 grant.Action != PermissionAction.View));
        }

        public bool CanUseModule(
            User user,
            string moduleKey,
            string requiredAccessLevel = PermissionAccessLevel.View)
        {
            if (user == null || !PermissionAccessLevel.IsKnown(requiredAccessLevel))
            {
                return false;
            }

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

        public bool CanUsePermission(User user, string resourceKey, string action)
        {
            if (user == null || !PermissionResourceCatalog.IsKnownAction(resourceKey, action))
            {
                return false;
            }

            var resource = PermissionResourceCatalog.ByKey[resourceKey.Trim()];
            if (!EditionIncludes(resource.Workspace))
            {
                return false;
            }

            if (RequiresAdministratorIdentity(resource.Key))
            {
                bool requiresFullEdition = resource.Key is
                    PermissionResourceCatalog.SystemDisasterRecovery or
                    PermissionResourceCatalog.SystemUsers or
                    PermissionResourceCatalog.SystemPermissions or
                    PermissionResourceCatalog.SystemAudit;
                return IsAdministrator(user) &&
                    (!requiresFullEdition ||
                     string.Equals(_productEdition, ProductEditionCatalog.Full, StringComparison.OrdinalIgnoreCase));
            }

            if (IsAdministrator(user))
            {
                return true;
            }

            return GetPermissionGrants(user).Any(grant =>
                string.Equals(grant.ResourceKey, resource.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(grant.Action, action, StringComparison.OrdinalIgnoreCase));
        }

        public string GetDataScope(User user, string resourceKey, string action)
        {
            if (!CanUsePermission(user, resourceKey, action))
            {
                return string.Empty;
            }

            if (IsAdministrator(user) || RequiresAdministratorIdentity(resourceKey))
            {
                return PermissionDataScope.All;
            }

            return GetPermissionGrants(user)
                .Where(grant => string.Equals(grant.ResourceKey, resourceKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(grant.Action, action, StringComparison.OrdinalIgnoreCase))
                .Select(grant => grant.DataScope)
                .OrderByDescending(PermissionDataScope.Rank)
                .FirstOrDefault() ?? string.Empty;
        }

        public IReadOnlyList<string> GetEnabledModules(User user)
        {
            return GetModuleAccess(user).Keys.ToArray();
        }

        public IReadOnlyDictionary<string, string> GetModuleAccess(User user)
        {
            return GetPermissionGrants(user)
                .Where(grant => PermissionResourceCatalog.ByKey.TryGetValue(grant.ResourceKey, out var resource) &&
                    PermissionModuleCatalog.ByKey.ContainsKey(resource.ModuleKey))
                .Select(grant => new
                {
                    PermissionResourceCatalog.ByKey[grant.ResourceKey].ModuleKey,
                    AccessLevel = PermissionResourceCatalog.GetNavigationAccessLevel(grant.ResourceKey, grant.Action)
                })
                .GroupBy(grant => grant.ModuleKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => PermissionModuleCatalog.ByKey[group.Key].SortOrder)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(grant => PermissionAccessLevel.Normalize(grant.AccessLevel))
                        .OrderByDescending(PermissionAccessLevel.Rank)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
        }

        public ApiUserCapabilitiesDto GetCapabilities(User user)
        {
            var moduleAccess = GetModuleAccess(user);
            var enabledModules = moduleAccess.Keys.ToArray();
            var permissions = GetPermissionGrants(user);
            return new ApiUserCapabilitiesDto(
                CanManageSettings(user),
                CanManageUsers(user),
                CanViewAllBusinessData(user),
                CanUseDocumentWorkspace(user),
                CanUseSalesWorkspace(user),
                _productEdition,
                enabledModules,
                moduleAccess.Select(grant => new ApiModuleAccessDto(grant.Key, grant.Value)).ToArray(),
                permissions.Select(grant => new ApiPermissionGrantDto(
                    grant.ResourceKey, grant.Action, grant.DataScope)).ToArray());
        }

        public IReadOnlyList<EffectivePermissionGrant> GetPermissionGrants(User user)
        {
            IEnumerable<EffectivePermissionGrant> grants;
            if (IsAdministrator(user))
            {
                grants = PermissionResourceCatalog.Resources.SelectMany(resource =>
                    resource.Actions.Select(action => new EffectivePermissionGrant(
                        resource.Key, action.Key, PermissionDataScope.All, "administrator")));
            }
            else
            {
                var effectiveGrants = UserPermissionAccessResolver.ResolveEffectiveGrants(user);
                grants = PermissionResourceCatalog.Resources.SelectMany(resource =>
                    resource.Actions.Select(action =>
                    {
                        string key = PermissionResourceCatalog.CreateGrantKey(resource.Key, action.Key);
                        return effectiveGrants.TryGetValue(key, out string? scope)
                            ? new EffectivePermissionGrant(resource.Key, action.Key, scope, "template")
                            : null;
                    }))
                    .OfType<EffectivePermissionGrant>();
            }
            return grants
                .Where(grant => PermissionResourceCatalog.ByKey.TryGetValue(grant.ResourceKey, out var resource) &&
                    EditionIncludes(resource.Workspace) &&
                    PermissionDataScope.IsKnown(grant.DataScope))
                .GroupBy(grant => PermissionResourceCatalog.CreateGrantKey(grant.ResourceKey, grant.Action),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(grant => PermissionDataScope.Rank(grant.DataScope)).First())
                .OrderBy(grant => PermissionResourceCatalog.ByKey[grant.ResourceKey].SortOrder)
                .ThenBy(grant => PermissionResourceCatalog.ByKey[grant.ResourceKey].Actions.Single(action =>
                    string.Equals(action.Key, grant.Action, StringComparison.OrdinalIgnoreCase)).SortOrder)
                .ToArray();
        }

        private bool EditionIncludes(string workspace) =>
            workspace == "common" ||
            workspace == "document" && ProductEditionCatalog.IncludesDocumentWorkspace(_productEdition) ||
            workspace == "sales" && ProductEditionCatalog.IncludesSalesWorkspace(_productEdition);

        private static bool RequiresAdministratorIdentity(string resourceKey) => resourceKey is
            PermissionResourceCatalog.SystemUsers or
            PermissionResourceCatalog.SystemPermissions or
            PermissionResourceCatalog.SystemSettings or
            PermissionResourceCatalog.SystemAudit or
            PermissionResourceCatalog.SystemBackup or
            PermissionResourceCatalog.SystemDisasterRecovery or
            PermissionResourceCatalog.EmailPolicy;

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

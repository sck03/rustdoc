using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Security
{
    public static class UserPermissionAccessResolver
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyGrants =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyDictionary<string, string> ResolveEffectiveGrants(User? user)
        {
            if (user == null) return EmptyGrants;

            // Only an unresolved built-in role may inherit defaults. An assigned
            // template or a populated grant set is authoritative, including omissions.
            if (user.EffectivePermissionGrants.Count > 0 || user.PermissionTemplateId != null)
            {
                return user.EffectivePermissionGrants;
            }

            try
            {
                return ResolveBuiltInGrants(user.Role);
            }
            catch (ArgumentException)
            {
                return EmptyGrants;
            }
        }

        public static void PopulateEffectivePermissions(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            if (user.PermissionTemplate != null)
            {
                user.EffectivePermissionGrants = user.PermissionTemplate.IsActive
                    ? ResolvePersistedGrants(user.PermissionTemplate.Grants)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            if (user.PermissionTemplateId == null)
            {
                user.EffectivePermissionGrants = ResolveBuiltInGrants(user.Role);
                return;
            }

            user.EffectivePermissionGrants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, string> ResolveBuiltInGrants(string role) =>
            BuiltInPermissionTemplateCatalog.FindForRole(role)
                .GetEffectivePermissions()
                .ToDictionary(
                    grant => PermissionResourceCatalog.CreateGrantKey(grant.ResourceKey, grant.Action),
                    grant => grant.DataScope,
                    StringComparer.OrdinalIgnoreCase);

        private static IReadOnlyDictionary<string, string> ResolvePersistedGrants(
            IEnumerable<PermissionTemplateGrant> persisted)
        {
            var direct = persisted
                .Where(grant => PermissionResourceCatalog.IsKnownAction(grant.ResourceKey, grant.Action) &&
                    PermissionDataScope.IsKnown(grant.DataScope))
                .GroupBy(grant => PermissionResourceCatalog.CreateGrantKey(grant.ResourceKey, grant.Action),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new PermissionGrantRecord(
                    group.First().ResourceKey,
                    group.First().Action,
                    group.Select(grant => PermissionDataScope.Normalize(grant.DataScope))
                        .OrderBy(PermissionDataScope.Rank)
                        .First()))
                .ToArray();

            return PermissionResourceCatalog.ExpandDependencies(direct)
                .GroupBy(grant => PermissionResourceCatalog.CreateGrantKey(grant.ResourceKey, grant.Action),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(grant => grant.DataScope)
                        .OrderByDescending(PermissionDataScope.Rank)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}

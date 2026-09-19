using System.Text.Json;
using System.ComponentModel;
using ExportDocManager.Models;
using ExportDocManager.Services.Security;
using Microsoft.OpenApi;

namespace ExportDocManager.Api.Hosting;

/// <summary>
/// Publishes the existing policy catalog for the native migration. This is
/// generated from the same definitions used by authorization, never a second
/// manually maintained route or permission directory.
/// </summary>
internal static class ApiNativeContractMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IOpenApiExtension Configuration()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        AddLabels(typeof(AppSettings), string.Empty, labels);
        return Extension(new { Defaults = new AppSettings(), Labels = labels });
    }

    private static void AddLabels(Type type, string prefix, IDictionary<string, string> labels)
    {
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(type))
        {
            string key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            string path = prefix.Length == 0 ? key : prefix + "." + key;
            if (property.DisplayName != property.Name) labels[path] = property.DisplayName;
            if (property.PropertyType.Namespace == typeof(AppSettings).Namespace && !property.PropertyType.IsEnum)
                AddLabels(property.PropertyType, path, labels);
        }
    }

    public static IOpenApiExtension PermissionCatalog()
    {
        var dependencies = PermissionResourceCatalog.Resources.SelectMany(resource =>
            resource.Actions.Select(action => new
            {
                ResourceKey = resource.Key,
                Action = action.Key,
                Grants = PermissionResourceCatalog.ExpandDependencies(
                    [new PermissionGrantRecord(resource.Key, action.Key, PermissionDataScope.All)])
                    .Where(grant => grant.Source == "dependency")
                    .Select(grant => new { grant.ResourceKey, grant.Action }).ToArray()
            })).Where(item => item.Grants.Length > 0).ToArray();
        return Extension(new
        {
            PermissionResourceCatalog.Resources,
            PermissionModuleCatalog.Modules,
            Roles = BuiltInPermissionTemplateCatalog.Templates,
            Dependencies = dependencies,
            Editions = ProductEditionCatalog.Editions.ToDictionary(edition => edition,
                edition => PermissionResourceCatalog.Resources
                    .Where(resource => ProductEditionCatalog.IncludesResource(edition, resource, false))
                    .Select(resource => resource.Key).ToArray())
        });
    }

    public static IOpenApiExtension EndpointPolicy(Endpoint endpoint)
    {
        var access = endpoint.GetApiAccessMetadata();
        var permissions = endpoint.GetApiPermissionMetadata();
        var capabilities = endpoint.GetApiCapabilityMetadata();
        return Extension(new
        {
            RequiresAuthentication = access?.RequiresAuthentication ?? false,
            RequiresDesktopAccess = access?.RequiresDesktopAccess ?? false,
            RequiresLicense = access?.RequiresLicense ?? false,
            Permissions = permissions,
            Requirements = capabilities?.Requirements ?? [],
            PermissionBypass = endpoint.HasExplicitPermissionBypass()
        });
    }

    private static IOpenApiExtension Extension<T>(T value) =>
        new JsonNodeExtension(JsonSerializer.SerializeToNode(value, JsonOptions)!);
}

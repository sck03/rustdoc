using Microsoft.AspNetCore.Builder;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting;

/// <summary>
/// The single source of truth for cross-cutting endpoint access policy.
/// Middleware and OpenAPI consume this metadata instead of maintaining
/// independent path-prefix allow/deny lists.
/// </summary>
internal sealed record ApiEndpointAccessMetadata(
    bool RequiresAuthentication,
    bool RequiresDesktopAccess,
    bool RequiresLicense = false);

internal enum ApiPermissionSelector
{
    Fixed,
    ReportType
}

internal sealed record ApiEndpointPermissionMetadata(
    string ReadModule,
    string? WriteModule = null,
    string? ReadAccessLevel = null,
    string? WriteAccessLevel = null,
    ApiPermissionSelector Selector = ApiPermissionSelector.Fixed,
    bool Disabled = false,
    // Group metadata is inherited by child endpoints.  A concrete endpoint
    // policy is marked as an override so resolution never depends on the
    // framework's incidental metadata ordering.
    bool OverridesParent = true)
{
    public (string Module, string AccessLevel) Resolve(HttpContext context)
    {
        bool isRead = HttpMethods.IsGet(context.Request.Method) ||
                      HttpMethods.IsHead(context.Request.Method) ||
                      HttpMethods.IsOptions(context.Request.Method);
        string module = isRead ? ResolveReadModule(context) : WriteModule ?? ReadModule;
        string accessLevel = isRead
            ? ReadAccessLevel ?? PermissionAccessLevel.View
            : WriteAccessLevel ?? (HttpMethods.IsDelete(context.Request.Method)
                ? PermissionAccessLevel.Manage
                : PermissionAccessLevel.Operate);
        return (module, accessLevel);
    }

    private string ResolveReadModule(HttpContext context)
    {
        if (Selector != ApiPermissionSelector.ReportType)
        {
            return ReadModule;
        }

        return context.Request.Query["reportType"].ToString() switch
        {
            var value when string.Equals(value, "PaymentVoucher", StringComparison.OrdinalIgnoreCase) =>
                PermissionModuleCatalog.DocumentPaymentReports,
            var value when string.Equals(value, "ExportDocument", StringComparison.OrdinalIgnoreCase) =>
                PermissionModuleCatalog.DocumentInvoiceReports,
            _ => ReadModule
        };
    }
}

internal sealed record ApiPermissionRequirement(string ResourceKey, string Action);

internal sealed record ApiEndpointCapabilityMetadata(
    IReadOnlyList<ApiPermissionRequirement> Requirements,
    bool OverridesParent = true);

internal static class ApiEndpointMetadataExtensions
{
    /// <summary>
    /// Explicitly marks an endpoint (usually an identity or administrative
    /// endpoint with its own handler-level authorization) as intentionally
    /// outside the capability middleware.  Missing metadata is otherwise a
    /// configuration error and is denied by the middleware.
    /// </summary>
    private sealed record ApiEndpointPermissionBypassMetadata;

    public static TBuilder WithApiAccess<TBuilder>(
        this TBuilder builder,
        ApiEndpointAccessMetadata metadata)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(metadata);
        return builder.WithMetadata(metadata);
    }

    public static TBuilder WithApiAccess<TBuilder>(
        this TBuilder builder,
        bool requiresAuthentication = true,
        bool requiresDesktopAccess = true,
        bool requiresLicense = true)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithApiAccess(new ApiEndpointAccessMetadata(
            requiresAuthentication, requiresDesktopAccess, requiresLicense));
    }

    public static TBuilder AllowAnonymousApi<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithApiAccess(false, false, false);

    public static TBuilder RequireBearerApi<TBuilder>(
        this TBuilder builder,
        bool requiresLicense = true)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithApiAccess(true, false, requiresLicense);

    public static TBuilder RequireDesktopApi<TBuilder>(
        this TBuilder builder,
        bool requiresAuthentication = true,
        bool requiresLicense = true)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithApiAccess(requiresAuthentication, true, requiresLicense);

    public static TBuilder WithApiPermission<TBuilder>(
        this TBuilder builder,
        string readModule,
        string? writeModule = null,
        string? readAccessLevel = null,
        string? writeAccessLevel = null,
        ApiPermissionSelector selector = ApiPermissionSelector.Fixed)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new ApiEndpointPermissionMetadata(
            readModule,
            writeModule,
            readAccessLevel,
            writeAccessLevel,
            selector,
            Disabled: false,
            OverridesParent: true));
    }

    public static TBuilder WithApiCapability<TBuilder>(
        this TBuilder builder,
        string resourceKey,
        string action)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithApiCapabilities(new ApiPermissionRequirement(resourceKey, action));

    public static TBuilder WithApiCapabilities<TBuilder>(
        this TBuilder builder,
        params ApiPermissionRequirement[] requirements)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (requirements == null || requirements.Length == 0 ||
            requirements.Any(requirement => !PermissionResourceCatalog.IsKnownAction(
                requirement.ResourceKey, requirement.Action)))
        {
            throw new ArgumentException("端点能力要求不能为空且必须来自正式权限目录。", nameof(requirements));
        }

        return builder.WithMetadata(new ApiEndpointCapabilityMetadata(requirements));
    }

    public static RouteGroupBuilder MapPermissionGroup(
        this IEndpointRouteBuilder endpoints,
        string readModule,
        string? writeModule = null,
        string? writeAccessLevel = null) =>
        endpoints.MapGroup(string.Empty).WithMetadata(new ApiEndpointPermissionMetadata(
            readModule,
            writeModule,
            WriteAccessLevel: writeAccessLevel,
            OverridesParent: false));

    public static TBuilder AllowApiWithoutPermission<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ApiEndpointPermissionBypassMetadata());

    public static ApiEndpointAccessMetadata? GetApiAccessMetadata(this Endpoint endpoint) =>
        Resolve(endpoint.Metadata.OfType<ApiEndpointAccessMetadata>());

    public static ApiEndpointPermissionMetadata? GetApiPermissionMetadata(this Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // Endpoint metadata is an ordered list containing inherited group
        // conventions and the concrete endpoint conventions.  Resolve the
        // most specific active policy explicitly; do not silently pick a
        // random/last item when multiple groups contribute metadata.
        var items = endpoint.Metadata
            .Select((value, index) => (value, index))
            .Where(item => item.value is ApiEndpointPermissionMetadata)
            .Select(item => (Metadata: (ApiEndpointPermissionMetadata)item.value, item.index))
            .ToArray();
        if (items.Length == 0)
        {
            return null;
        }

        var activeOverrides = items
            .Where(item => !item.Metadata.Disabled && item.Metadata.OverridesParent)
            .OrderBy(item => item.index)
            .ToArray();
        if (activeOverrides.Length > 0)
        {
            return activeOverrides[^1].Metadata;
        }

        return items
            .Where(item => !item.Metadata.Disabled)
            .OrderBy(item => item.index)
            .Select(item => item.Metadata)
            .LastOrDefault();
    }

    public static ApiEndpointCapabilityMetadata? GetApiCapabilityMetadata(this Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var policies = endpoint.Metadata
            .OfType<ApiEndpointCapabilityMetadata>()
            .ToArray();
        if (policies.Length == 0)
        {
            return null;
        }

        // Capability metadata is conjunctive: group requirements and every
        // concrete endpoint requirement must all succeed.  This differs from
        // legacy module metadata, where a concrete endpoint intentionally
        // replaces the HTTP-method-derived group policy.  Deduplication keeps
        // repeated group conventions deterministic without weakening them.
        var requirements = policies
            .SelectMany(policy => policy.Requirements)
            .DistinctBy(
                requirement => PermissionResourceCatalog.CreateGrantKey(
                    requirement.ResourceKey,
                    requirement.Action),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return requirements.Length == 0
            ? null
            : new ApiEndpointCapabilityMetadata(requirements);
    }

    public static bool HasExplicitPermissionBypass(this Endpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // A child permission policy must win over a bypass convention on a
        // parent group.  Compare the last convention of either kind.
        bool bypass = false;
        for (int index = 0; index < endpoint.Metadata.Count; index++)
        {
            object metadata = endpoint.Metadata[index];
            if (metadata is ApiEndpointPermissionMetadata or ApiEndpointCapabilityMetadata)
            {
                bypass = false;
            }
            else if (metadata is ApiEndpointPermissionBypassMetadata)
            {
                bypass = true;
            }
        }

        return bypass;
    }

    public static ApiEndpointAccessMetadata? GetApiAccessMetadata(this EndpointBuilder builder) =>
        Resolve(builder.Metadata.OfType<ApiEndpointAccessMetadata>());

    public static ApiEndpointAccessMetadata? Resolve(IEnumerable<ApiEndpointAccessMetadata> metadata)
    {
        var items = metadata.ToArray();
        if (items.Length == 0)
        {
            return null;
        }

        // Route groups provide the strict default policy. An endpoint may
        // explicitly relax an inherited requirement, so false wins while the
        // framework combines group and endpoint metadata.
        return new ApiEndpointAccessMetadata(
            items.All(item => item.RequiresAuthentication),
            items.All(item => item.RequiresDesktopAccess),
            items.All(item => item.RequiresLicense));
    }
}

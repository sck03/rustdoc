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
    bool Disabled = false)
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

internal static class ApiEndpointMetadataExtensions
{
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
            selector));
    }

    public static RouteGroupBuilder MapPermissionGroup(
        this IEndpointRouteBuilder endpoints,
        string readModule,
        string? writeModule = null,
        string? writeAccessLevel = null) =>
        endpoints.MapGroup(string.Empty).WithApiPermission(
            readModule,
            writeModule,
            writeAccessLevel: writeAccessLevel);

    public static TBuilder AllowApiWithoutPermission<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new ApiEndpointPermissionMetadata(string.Empty, Disabled: true));

    public static ApiEndpointAccessMetadata? GetApiAccessMetadata(this Endpoint endpoint) =>
        Resolve(endpoint.Metadata.OfType<ApiEndpointAccessMetadata>());

    public static ApiEndpointPermissionMetadata? GetApiPermissionMetadata(this Endpoint endpoint) =>
        endpoint.Metadata.OfType<ApiEndpointPermissionMetadata>().LastOrDefault() is { Disabled: false } metadata
            ? metadata
            : null;

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

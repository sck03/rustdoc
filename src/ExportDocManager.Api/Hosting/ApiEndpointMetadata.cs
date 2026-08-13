using Microsoft.AspNetCore.Builder;

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

    public static ApiEndpointAccessMetadata? GetApiAccessMetadata(this Endpoint endpoint) =>
        Resolve(endpoint.Metadata.OfType<ApiEndpointAccessMetadata>());

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

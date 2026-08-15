using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ExportDocManager.Api.Hosting;

internal sealed class ApiOpenApiDocumentTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private readonly ApiDesktopAccessOptions _desktopAccessOptions;
    private readonly ApiRuntimeOptions _runtimeOptions;

    public ApiOpenApiDocumentTransformer(
        ApiDesktopAccessOptions desktopAccessOptions,
        ApiRuntimeOptions runtimeOptions)
    {
        _desktopAccessOptions = desktopAccessOptions ?? throw new ArgumentNullException(nameof(desktopAccessOptions));
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        document.Info ??= new OpenApiInfo();
        document.Info.Title = "ExportDocManager API";
        document.Info.Version = ProductVersionProvider.ProductVersion;
        document.Info.Description = "Local sidecar API for ExportDocManager desktop and browser clients.";
        document.Servers = [new OpenApiServer
        {
            Url = string.IsNullOrEmpty(_runtimeOptions.PathBase) ? "/" : _runtimeOptions.PathBase
        }];

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "opaque",
            Description = "Use the accessToken returned by /api/auth/login as a Bearer token."
        };
        document.Components.SecuritySchemes["DesktopAccess"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiDesktopAccessOptions.HeaderName,
            Description = "Internal desktop sidecar token used by lifecycle and local-file endpoints."
        };

        return Task.CompletedTask;
    }
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = ApiEndpointMetadataExtensions.Resolve(
            context.Description.ActionDescriptor.EndpointMetadata.OfType<ApiEndpointAccessMetadata>());
        bool requiresBearer = metadata?.RequiresAuthentication ?? false;
        bool requiresDesktop = _desktopAccessOptions.IsEnabled &&
            (metadata?.RequiresDesktopAccess ?? false);
        if (!requiresBearer && !requiresDesktop)
        {
            operation.Security = null;
            return Task.CompletedTask;
        }
        var requirements = new List<OpenApiSecurityRequirement>();

        var requirement = new OpenApiSecurityRequirement();
        if (requiresBearer)
        {
            requirement[new OpenApiSecuritySchemeReference("BearerAuth", context.Document, null)] = [];
        }
        if (requiresDesktop)
        {
            requirement[new OpenApiSecuritySchemeReference("DesktopAccess", context.Document, null)] = [];
        }
        requirements.Add(requirement);
        operation.Security = requirements;
        return Task.CompletedTask;
    }
}

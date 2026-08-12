using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ExportDocManager.Api.Hosting;

internal sealed class ApiOpenApiDocumentTransformer(ApiRuntimeOptions runtimeOptions) : IOpenApiDocumentTransformer
{
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
        document.Servers = runtimeOptions.ListenUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(url => new OpenApiServer { Url = url })
            .ToList();

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
}

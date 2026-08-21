using System.Text.Json.Nodes;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

internal static class OfficialOpenApiDocumentLoader
{
    private const string DocumentName = "v1";

    public static async Task<JsonObject> LoadAsync(string baseUrl)
    {
        string repositoryRoot = Directory.GetCurrentDirectory();
        string dataRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "api-client-openapi",
            Guid.NewGuid().ToString("N"));
        var pathProvider = new RuntimeAppPathProvider(repositoryRoot, dataRoot);
        var databaseSettings = new DatabaseConnectionSettings
        {
            Provider = DatabaseConnectionSettings.SqliteProvider,
            SqliteDatabaseFileName = "api-client.db"
        };
        var runtimeOptions = new ApiRuntimeOptions
        {
            AppRoot = repositoryRoot,
            DataRoot = dataRoot,
            ListenUrls = baseUrl
        };

        WebApplication? app = null;
        try
        {
            ApiStartupValidator.Validate(pathProvider, databaseSettings, runtimeOptions);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = repositoryRoot
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddExportDocManagerApiServices(pathProvider, databaseSettings, runtimeOptions);

            app = builder.Build();
            app.MapExportDocManagerApiEndpoints(runtimeOptions, databaseSettings);
            await app.StartAsync();

            var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentName);
            var openApiDocument = await provider.GetOpenApiDocumentAsync(CancellationToken.None);
            string json = await openApiDocument.SerializeAsJsonAsync(
                OpenApiSpecVersion.OpenApi3_1,
                CancellationToken.None);
            return JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("Official OpenAPI document could not be parsed.");
        }
        finally
        {
            if (app != null)
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }

            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }
}

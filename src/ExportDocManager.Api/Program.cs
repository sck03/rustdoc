using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Tools;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var runtimeOptions = ApiRuntimeOptions.Parse(args);
var pathProvider = string.IsNullOrWhiteSpace(runtimeOptions.DataRoot)
    ? new RuntimeAppPathProvider(runtimeOptions.AppRoot)
    : new RuntimeAppPathProvider(runtimeOptions.AppRoot, runtimeOptions.DataRoot);

// Validate the complete existing path chain before any runtime directory,
// recovery marker, cache file or database setting can be created.
ApiStartupValidator.PrepareRuntimeDirectories(pathProvider);
DbHelper.ConfigurePathProvider(pathProvider);
// ASP.NET Core form buffering otherwise falls back to the process/system temp
// directory for multipart requests.  Keep that transient data under the same
// runtime root as uploads, browser profiles and other disposable caches.
string aspNetTempRoot = Path.Combine(pathProvider.CacheRoot, "AspNetTemp");
Directory.CreateDirectory(aspNetTempRoot);
Environment.SetEnvironmentVariable("ASPNETCORE_TEMP", aspNetTempRoot);
if (args.Any(value => string.Equals(value, "--verify-ocr-runtime", StringComparison.OrdinalIgnoreCase)))
{
    var verification = await OcrRuntimeVerifier.VerifyAsync(pathProvider);
    Console.WriteLine($"PP-OCRv6 verification passed. Platform={verification.Platform}; Engine={verification.Engine}; Text={verification.RecognizedText}");
    return;
}

await ServerMigrationRecoveryStateMachine.ApplyAsync(pathProvider);
SingleWindowDisasterRecoveryManager.ApplyPendingRestore(pathProvider);
var databaseSettings = DbHelper.LoadDatabaseSettings();
ApiStartupValidator.Validate(pathProvider, databaseSettings, runtimeOptions);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(runtimeOptions.ListenUrls);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = ApiUploadLimits.MaximumRequestBodyBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ApiUploadLimits.MaximumRequestBodyBytes;
    options.MemoryBufferThreshold = 64 * 1024;
    options.MultipartHeadersLengthLimit = 64 * 1024;
    options.ValueCountLimit = 1000;
});
builder.Services.AddExportDocManagerApiServices(pathProvider, databaseSettings, runtimeOptions);

await using var app = builder.Build();
app.UseExportDocManagerForwardedHeaders(runtimeOptions);
app.UseExportDocManagerApiSafety();
app.UseCors(ApiCorsPolicy.LocalFrontendPolicyName);
app.UseExportDocManagerReadiness(databaseSettings, runtimeOptions);
app.UseExportDocManagerDesktopAccess();
app.UseExportDocManagerApiAuthentication();
app.UseExportDocManagerWorkspaceAccess();
app.UseExportDocManagerLicenseRequirement();
app.UseExportDocManagerBrowserFrontend(pathProvider.AppRoot);
app.MapExportDocManagerApiEndpoints(runtimeOptions, databaseSettings);
app.MapExportDocManagerBrowserFallback(pathProvider.AppRoot);
await app.StartAsync();
try
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? app.Urls;
    ApiEndpointPublication.Publish(runtimeOptions.EndpointFile, addresses);
    await app.WaitForShutdownAsync();
}
finally
{
    ApiEndpointPublication.Remove(runtimeOptions.EndpointFile);
}

public partial class Program
{
}

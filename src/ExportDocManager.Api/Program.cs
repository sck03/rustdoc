using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Tools;
using ExportDocManager.Utils;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var runtimeOptions = ApiRuntimeOptions.Parse(args);
var pathProvider = string.IsNullOrWhiteSpace(runtimeOptions.DataRoot)
    ? new RuntimeAppPathProvider(runtimeOptions.AppRoot)
    : new RuntimeAppPathProvider(runtimeOptions.AppRoot, runtimeOptions.DataRoot);

// Validate the complete existing path chain before any runtime directory,
// recovery marker, cache file or database setting can be created.
ApiStartupValidator.PrepareRuntimeDirectories(pathProvider);
// ASP.NET Core form buffering otherwise falls back to the process/system temp
// directory for multipart requests.  Keep that transient data under the same
// runtime root as uploads, browser profiles and other disposable caches.
string aspNetTempRoot = Path.Combine(pathProvider.CacheRoot, "AspNetTemp");
Directory.CreateDirectory(aspNetTempRoot);
Environment.SetEnvironmentVariable("ASPNETCORE_TEMP", aspNetTempRoot);
await ServerMigrationRecoveryStateMachine.ApplyAsync(pathProvider);
SingleWindowDisasterRecoveryManager.ApplyPendingRestore(pathProvider);
var databaseSettings = DbHelper.LoadDatabaseSettings(pathProvider);
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
if (args.Any(value => string.Equals(value, "--verify-ocr-runtime", StringComparison.OrdinalIgnoreCase)))
{
    var verifier = app.Services.GetService<IOcrRuntimeVerifier>()
        ?? throw new InvalidOperationException("当前发布包未包含 PDF/OCR 能力模块。");
    var verification = await verifier.VerifyAsync();
    Console.WriteLine($"PP-OCRv6 verification passed. Platform={verification.Platform}; Engine={verification.Engine}; Text={verification.RecognizedText}");
    return;
}
if (args.Any(value => string.Equals(value, "--verify-browser-runtime", StringComparison.OrdinalIgnoreCase)))
{
    await VerifyBrowserRuntimeAsync(app.Services, pathProvider);
    return;
}

app.UseExportDocManagerForwardedHeaders(runtimeOptions);
app.UseExportDocManagerApiSafety();
app.UseCors(ApiCorsPolicy.LocalFrontendPolicyName);
app.UseExportDocManagerReadiness(databaseSettings, runtimeOptions);
// Endpoint access metadata must be available to the cross-cutting middleware
// and to the endpoint itself.  Route selection therefore intentionally occurs
// before authentication/license/workspace policy evaluation.
app.UseRouting();
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

static async Task VerifyBrowserRuntimeAsync(
    IServiceProvider services,
    IAppPathProvider pathProvider)
{
    string verificationRoot = Path.Combine(
        pathProvider.CacheRoot,
        "BrowserRuntimeVerification",
        Guid.NewGuid().ToString("N"));
    string destinationPath = Path.Combine(verificationRoot, "probe.pdf");
    try
    {
        Directory.CreateDirectory(verificationRoot);
        using IServiceScope scope = services.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IHtmlToPdfService>();
        var result = await renderer.RenderAsync(
                "<!doctype html><html><head><meta charset=\"utf-8\"></head>" +
                "<body><h1>ExportDocManager Browser Runtime</h1>" +
                "<p>Remote CDP PDF bridge validation</p></body></html>",
                destinationPath,
                new HtmlToPdfRenderOptions { DocumentTitle = "ExportDocManager Browser Runtime" })
            .ConfigureAwait(false);

        using var pdf = File.OpenRead(destinationPath);
        Span<byte> signature = stackalloc byte[5];
        int read = pdf.Read(signature);
        if (read != signature.Length ||
            !signature.SequenceEqual("%PDF-"u8))
        {
            throw new InvalidDataException("Browser runtime did not produce a valid PDF signature.");
        }

        Console.WriteLine(
            $"Browser runtime verification passed. Renderer={result.RendererPath}; Bytes={pdf.Length}");
    }
    finally
    {
        AtomicFileHelper.TryDeleteDirectory(verificationRoot);
    }
}

public partial class Program
{
}

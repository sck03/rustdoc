using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Tools;

var runtimeOptions = ApiRuntimeOptions.Parse(args);
var pathProvider = string.IsNullOrWhiteSpace(runtimeOptions.DataRoot)
    ? new RuntimeAppPathProvider(runtimeOptions.AppRoot)
    : new RuntimeAppPathProvider(runtimeOptions.AppRoot, runtimeOptions.DataRoot);

DbHelper.ConfigurePathProvider(pathProvider);
if (args.Any(value => string.Equals(value, "--verify-ocr-runtime", StringComparison.OrdinalIgnoreCase)))
{
    var verification = await OcrRuntimeVerifier.VerifyAsync(pathProvider);
    Console.WriteLine($"PP-OCRv6 verification passed. Platform={verification.Platform}; Engine={verification.Engine}; Text={verification.RecognizedText}");
    return;
}

var databaseSettings = DbHelper.LoadDatabaseSettings();
ApiStartupValidator.Validate(pathProvider, databaseSettings, runtimeOptions);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(runtimeOptions.ListenUrls);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = ApiUploadLimits.MaximumRequestBodyBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = ApiUploadLimits.MaximumRequestBodyBytes);
builder.Services.AddExportDocManagerApiServices(pathProvider, databaseSettings, runtimeOptions);

var app = builder.Build();
app.UseExportDocManagerForwardedHeaders(runtimeOptions);
app.UseExportDocManagerApiSafety();
app.UseCors(ApiCorsPolicy.LocalFrontendPolicyName);
app.UseExportDocManagerDesktopAccess();
app.UseExportDocManagerApiAuthentication();
app.UseExportDocManagerWorkspaceAccess();
app.UseExportDocManagerLicenseRequirement();
app.UseExportDocManagerBrowserFrontend(pathProvider.AppRoot);
app.MapExportDocManagerApiEndpoints(runtimeOptions, databaseSettings);
app.MapExportDocManagerBrowserFallback(pathProvider.AppRoot);
app.Run();

public partial class Program
{
}

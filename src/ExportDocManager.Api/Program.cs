using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;

var runtimeOptions = ApiRuntimeOptions.Parse(args);
var pathProvider = string.IsNullOrWhiteSpace(runtimeOptions.DataRoot)
    ? new RuntimeAppPathProvider(runtimeOptions.AppRoot)
    : new RuntimeAppPathProvider(runtimeOptions.AppRoot, runtimeOptions.DataRoot);

DbHelper.ConfigurePathProvider(pathProvider);
var databaseSettings = DbHelper.LoadDatabaseSettings();
ApiStartupValidator.Validate(pathProvider, databaseSettings, runtimeOptions);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(runtimeOptions.ListenUrls);
builder.Services.AddExportDocManagerApiServices(pathProvider, databaseSettings, runtimeOptions);

var app = builder.Build();
app.UseCors(ApiCorsPolicy.LocalFrontendPolicyName);
app.UseExportDocManagerDesktopAccess();
app.UseExportDocManagerApiAuthentication();
app.UseExportDocManagerWorkspaceAccess();
app.UseExportDocManagerLicenseRequirement();
app.MapExportDocManagerApiEndpoints(runtimeOptions, databaseSettings);
app.Run();

public partial class Program
{
}

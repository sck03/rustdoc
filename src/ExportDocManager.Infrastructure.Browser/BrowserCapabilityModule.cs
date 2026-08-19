using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Infrastructure.Browser;

public sealed class BrowserCapabilityModule : IExportDocCapabilityModule
{
    public string Key => "browser";

    public void RegisterServices(
        IExportDocCapabilityRegistry services,
        IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pathProvider);

        services.AddSingleton<BrowserRuntimeManager>();
        services.AddSingleton<BrowserExecutableResolver>();
        services.AddSingleton(provider => new ManagedPlaywrightBrowserHost(
            provider.GetRequiredService<BrowserRuntimeManager>(),
            provider.GetRequiredService<BrowserExecutableResolver>(),
            pathProvider,
            BrowserNavigationPolicy.I5a6Only));
        services.AddSingleton<ManagedPlaywrightPdfBrowserHost>();
        services.AddSingleton<IHsCodeRemoteProvider, I5a6HsCodeProvider>();
        services.AddScoped<IHtmlToPdfService>(provider => new ChromiumHtmlToPdfService(
            pathProvider,
            provider.GetRequiredService<BrowserRuntimeManager>(),
            provider.GetRequiredService<ManagedPlaywrightPdfBrowserHost>(),
            provider.GetRequiredService<BrowserExecutableResolver>()));
        services.AddSingleton<IRuntimeDependencyDiagnosticContributor, BrowserRuntimeDiagnosticContributor>();
        services.AddSingleton<IBrowserRuntimeProbe, BrowserRuntimeProbe>();
    }
}

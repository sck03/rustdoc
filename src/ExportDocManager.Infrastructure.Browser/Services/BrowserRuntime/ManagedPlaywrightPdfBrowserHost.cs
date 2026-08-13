using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.BrowserRuntime;

/// <summary>
/// Dedicated local-file browser process for report rendering. It intentionally
/// cannot navigate to HTTP(S) resources and is never shared with HS web
/// automation.
/// </summary>
public sealed class ManagedPlaywrightPdfBrowserHost : ManagedPlaywrightBrowserHost
{
    public ManagedPlaywrightPdfBrowserHost(
        BrowserRuntimeManager runtime,
        BrowserExecutableResolver resolver,
        IAppPathProvider pathProvider)
        : base(runtime, resolver, pathProvider, BrowserNavigationPolicy.LocalFilesOnly)
    {
    }
}

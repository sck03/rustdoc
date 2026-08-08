namespace ExportDocManager.Services.BrowserRuntime;

/// <summary>
/// Network boundary applied to each managed browser context. Keeping this policy
/// at the context boundary prevents an external automation page from sharing the
/// local-file privileges required by report rendering.
/// </summary>
public enum BrowserNavigationPolicy
{
    /// <summary>Retained for parser/unit-test hosts that provide their own content.</summary>
    Unrestricted,

    /// <summary>Only local document schemes are allowed (report/PDF rendering).</summary>
    LocalFilesOnly,

    /// <summary>Only the reviewed i5a6 HTTPS origin is allowed (HS lookup fallback).</summary>
    I5a6Only
}

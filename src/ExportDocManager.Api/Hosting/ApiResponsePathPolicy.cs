namespace ExportDocManager.Api.Hosting;

/// <summary>
/// Server filesystem paths are operational details, not browser API data.
/// They are returned only to the authenticated local Tauri sidecar channel.
/// Browser and container clients receive stable logical names and empty path fields.
/// </summary>
internal static class ApiResponsePathPolicy
{
    public static bool CanReveal(HttpContext context, ApiDesktopAccessOptions options) =>
        options?.IsEnabled == true && ApiEndpointAuth.HasValidDesktopAccess(context, options);

    public static string Reveal(string path, bool canReveal) =>
        canReveal ? path ?? string.Empty : string.Empty;
}

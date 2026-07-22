using Microsoft.Extensions.FileProviders;

namespace ExportDocManager.Api.Hosting
{
    public static class BrowserFrontendHostingExtensions
    {
        private static readonly string[] ReservedPathPrefixes = ["/api", "/openapi", "/healthz", "/readyz"];

        public static WebApplication UseExportDocManagerBrowserFrontend(
            this WebApplication app,
            string appRoot)
        {
            string webRoot = Path.Combine(appRoot, "wwwroot");
            if (!File.Exists(Path.Combine(webRoot, "index.html")))
            {
                return app;
            }

            var fileProvider = new PhysicalFileProvider(webRoot);
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = fileProvider
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider
            });
            return app;
        }

        public static WebApplication MapExportDocManagerBrowserFallback(
            this WebApplication app,
            string appRoot)
        {
            string indexPath = Path.Combine(appRoot, "wwwroot", "index.html");
            if (!File.Exists(indexPath))
            {
                return app;
            }

            app.MapFallback(async context =>
            {
                bool reserved = ReservedPathPrefixes.Any(prefix =>
                    context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
                bool acceptsHtml = context.Request.Method == HttpMethods.Get &&
                    context.Request.GetTypedHeaders().Accept?.Any(value =>
                        value.MediaType.Value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true) != false;

                if (reserved || !acceptsHtml)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(indexPath);
            });
            return app;
        }
    }
}

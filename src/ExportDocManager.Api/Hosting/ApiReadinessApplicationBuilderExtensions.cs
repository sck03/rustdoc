namespace ExportDocManager.Api.Hosting
{
    /// <summary>
    /// Handles the process-level readiness probe before authentication,
    /// licensing, workspace authorization, static-file hosting, or database
    /// services are resolved.  Container orchestration must be able to tell
    /// whether Kestrel is responsive even before the shared database has been
    /// initialized by the first administrator login.
    /// </summary>
    public static class ApiReadinessApplicationBuilderExtensions
    {
        private const string ReadinessPath = "/readyz";
        private const string ReadinessPayload = "{\"status\":\"ok\"}";

        public static IApplicationBuilder UseExportDocManagerReadiness(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.Use(async (context, next) =>
            {
                if (!context.Request.Path.Equals(ReadinessPath, StringComparison.OrdinalIgnoreCase))
                {
                    await next(context).ConfigureAwait(false);
                    return;
                }

                if (!HttpMethods.IsGet(context.Request.Method) &&
                    !HttpMethods.IsHead(context.Request.Method))
                {
                    context.Response.Headers.Allow = "GET, HEAD";
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";
                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.WriteAsync(ReadinessPayload, context.RequestAborted)
                        .ConfigureAwait(false);
                }
            });
        }
    }
}

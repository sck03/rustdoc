using System.Text.Json;
using ExportDocManager.DataAccess;

namespace ExportDocManager.Api.Hosting
{
    /// <summary>
    /// Handles process-level readiness and anonymous health probes before
    /// authentication, licensing, workspace authorization, static-file
    /// hosting, the endpoint route table, or database services are resolved.
    /// Container orchestration must be able to tell whether Kestrel is
    /// responsive even before the shared database has been initialized by the
    /// first administrator login.  Requests carrying a Bearer or trusted
    /// desktop token continue through the normal detailed health endpoint.
    /// </summary>
    public static class ApiReadinessApplicationBuilderExtensions
    {
        private const string ReadinessPath = "/readyz";
        private const string LivenessPath = "/livez";
        private const string LivenessPayload = "{\"status\":\"alive\"}";
        private const string HealthPath = "/healthz";
        private static readonly JsonSerializerOptions ProbeJsonOptions = JsonSerializerOptions.Web;

        public static IApplicationBuilder UseExportDocManagerReadiness(
            this IApplicationBuilder app,
            DatabaseConnectionSettings? databaseSettings = null,
            ApiRuntimeOptions? runtimeOptions = null)
        {
            ArgumentNullException.ThrowIfNull(app);
            var desktopAccessOptions = runtimeOptions == null
                ? null
                : ApiDesktopAccessOptions.FromRuntimeOptions(runtimeOptions);

            return app.Use(async (context, next) =>
            {
                if (context.Request.Path.Equals(LivenessPath, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteLivenessAsync(context).ConfigureAwait(false);
                    return;
                }

                if (context.Request.Path.Equals(ReadinessPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Readiness must cross the authentication service graph. This catches
                    // dependency cycles that a process-only health response would hide.
                    _ = context.RequestServices.GetService<ApiCurrentUserResolver>();
                    IApiReadinessProbe readinessProbe =
                        context.RequestServices.GetRequiredService<IApiReadinessProbe>();
                    await WriteReadinessAsync(context, readinessProbe).ConfigureAwait(false);
                    return;
                }

                if (databaseSettings != null &&
                    ShouldHandlePublicHealthProbe(context, desktopAccessOptions))
                {
                    await WritePublicHealthAsync(context, databaseSettings).ConfigureAwait(false);
                    return;
                }

                await next(context).ConfigureAwait(false);
            });
        }

        internal static bool ShouldHandlePublicHealthProbe(
            HttpContext context,
            ApiDesktopAccessOptions? desktopAccessOptions)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!context.Request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase) ||
                (!HttpMethods.IsGet(context.Request.Method) &&
                 !HttpMethods.IsHead(context.Request.Method)))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ApiCurrentUserResolver.GetBearerToken(context)))
            {
                return false;
            }

            return desktopAccessOptions == null ||
                !ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions);
        }

        internal static async Task WriteLivenessAsync(HttpContext context)
        {
            if (!EnsureProbeMethod(context))
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.WriteAsync(LivenessPayload, context.RequestAborted)
                    .ConfigureAwait(false);
            }
        }

        internal static async Task WriteReadinessAsync(
            HttpContext context,
            IApiReadinessProbe readinessProbe)
        {
            if (!EnsureProbeMethod(context))
            {
                return;
            }

            ApiReadinessSnapshot snapshot =
                await readinessProbe.CheckAsync(context.RequestAborted).ConfigureAwait(false);

            context.Response.StatusCode = snapshot.Ready
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json; charset=utf-8";
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await JsonSerializer.SerializeAsync(
                        context.Response.Body,
                        new
                        {
                            status = snapshot.Ready ? "ready" : "not_ready",
                            checkedAt = snapshot.CheckedAt,
                            checks = snapshot.Checks
                        },
                        ProbeJsonOptions,
                        context.RequestAborted)
                    .ConfigureAwait(false);
            }
        }

        private static bool EnsureProbeMethod(HttpContext context)
        {
            if (HttpMethods.IsGet(context.Request.Method) ||
                HttpMethods.IsHead(context.Request.Method))
            {
                return true;
            }

            context.Response.Headers.Allow = "GET, HEAD";
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return false;
        }

        private static async Task WritePublicHealthAsync(
            HttpContext context,
            DatabaseConnectionSettings databaseSettings)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            if (HttpMethods.IsHead(context.Request.Method))
            {
                return;
            }

            await JsonSerializer.SerializeAsync(
                    context.Response.Body,
                    ApiHealthResponseFactory.CreatePublic(databaseSettings),
                    ProbeJsonOptions,
                    context.RequestAborted)
                .ConfigureAwait(false);
        }
    }
}

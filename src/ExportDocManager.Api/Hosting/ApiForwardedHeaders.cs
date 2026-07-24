using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace ExportDocManager.Api.Hosting
{
    /// <summary>
    /// Configures forwarded headers without turning the application into an open proxy-trust boundary.
    /// ASP.NET Core's safe loopback defaults are retained; deployments add only explicitly configured
    /// reverse-proxy addresses (for example, the fixed Nginx address in the container Compose files).
    /// </summary>
    public static class ApiForwardedHeaders
    {
        public static ForwardedHeadersOptions CreateOptions(ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);

            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                // Each forwarded hop is still checked against KnownProxies/KnownNetworks. The
                // explicit count permits a configured HTTPS proxy in front of the bundled Nginx
                // without accepting an unbounded client-supplied forwarding chain.
                ForwardLimit = Math.Max(1, runtimeOptions.TrustedProxies.Count)
            };

            foreach (var proxy in runtimeOptions.TrustedProxies)
            {
                if (!options.KnownProxies.Contains(proxy))
                {
                    options.KnownProxies.Add(proxy);
                }
            }

            return options;
        }

        public static IApplicationBuilder UseExportDocManagerForwardedHeaders(
            this IApplicationBuilder app,
            ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.UseForwardedHeaders(CreateOptions(runtimeOptions));
            return app;
        }
    }
}

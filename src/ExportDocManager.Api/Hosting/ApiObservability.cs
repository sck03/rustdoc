using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ExportDocManager.Api.Hosting;

internal static class ApiObservabilityExtensions
{
    private const string ServiceName = "ExportDocManager.Api";
    private const string OtlpEndpointEnvironmentVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static IServiceCollection AddExportDocManagerObservability(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Uri? exporterEndpoint = ResolveOptionalOtlpEndpoint();

        OpenTelemetryBuilder telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                ServiceName,
                serviceVersion: ProductVersionProvider.ProductVersion));

        telemetry.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();
            if (exporterEndpoint != null)
            {
                tracing.AddOtlpExporter(options => options.Endpoint = exporterEndpoint);
            }
        });

        telemetry.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();
            if (exporterEndpoint != null)
            {
                metrics.AddOtlpExporter(options => options.Endpoint = exporterEndpoint);
            }
        });

        return services;
    }

    internal static Uri? ResolveOptionalOtlpEndpoint()
    {
        return ResolveOptionalOtlpEndpoint(
            Environment.GetEnvironmentVariable(OtlpEndpointEnvironmentVariable));
    }

    internal static Uri? ResolveOptionalOtlpEndpoint(string? configuredValue)
    {
        string value = configuredValue?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"{OtlpEndpointEnvironmentVariable} 必须是绝对 HTTP(S) 地址。");
        }

        return endpoint;
    }
}

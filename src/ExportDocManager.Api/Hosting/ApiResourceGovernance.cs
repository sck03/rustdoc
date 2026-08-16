using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;

namespace ExportDocManager.Api.Hosting;

internal enum ApiResourceProfile
{
    Interactive,
    Authentication,
    Workload,
    Maintenance,
    Streaming
}

internal sealed record ApiEndpointResourceMetadata(ApiResourceProfile Profile);

internal static class ApiResourcePolicyCatalog
{
    public const string InteractiveTimeoutPolicy = "api-interactive";
    public const string AuthenticationTimeoutPolicy = "api-authentication";
    public const string WorkloadTimeoutPolicy = "api-workload";
    public const string MaintenanceTimeoutPolicy = "api-maintenance";
    public const string StreamingTimeoutPolicy = "api-streaming";

    public static string GetTimeoutPolicyName(ApiResourceProfile profile) => profile switch
    {
        ApiResourceProfile.Authentication => AuthenticationTimeoutPolicy,
        ApiResourceProfile.Workload => WorkloadTimeoutPolicy,
        ApiResourceProfile.Maintenance => MaintenanceTimeoutPolicy,
        ApiResourceProfile.Streaming => StreamingTimeoutPolicy,
        _ => InteractiveTimeoutPolicy
    };

    public static ApiResourceLimits GetLimits(ApiResourceProfile profile) => profile switch
    {
        ApiResourceProfile.Authentication => new(30, 6, 2),
        ApiResourceProfile.Workload => new(90, 12, 4),
        ApiResourceProfile.Maintenance => new(20, 2, 1),
        ApiResourceProfile.Streaming => new(60, 12, 4),
        _ => new(300, 64, 16)
    };

    public static ApiResourceProfile? Resolve(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ApiEndpointResourceMetadata>()?.Profile;
}

internal readonly record struct ApiResourceLimits(
    int RequestsPerMinute,
    int ConcurrentRequests,
    int QueueLimit);

internal static class ApiResourceGovernanceExtensions
{
    public static TBuilder WithApiResourceProfile<TBuilder>(
        this TBuilder builder,
        ApiResourceProfile profile)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new ApiEndpointResourceMetadata(profile));
        builder.WithRequestTimeout(ApiResourcePolicyCatalog.GetTimeoutPolicyName(profile));
        return builder;
    }

    public static IServiceCollection AddExportDocManagerResourceGovernance(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRequestTimeouts(options =>
        {
            options.AddPolicy(ApiResourcePolicyCatalog.InteractiveTimeoutPolicy, TimeSpan.FromMinutes(1));
            options.AddPolicy(ApiResourcePolicyCatalog.AuthenticationTimeoutPolicy, TimeSpan.FromSeconds(20));
            options.AddPolicy(ApiResourcePolicyCatalog.WorkloadTimeoutPolicy, TimeSpan.FromMinutes(5));
            options.AddPolicy(ApiResourcePolicyCatalog.MaintenanceTimeoutPolicy, TimeSpan.FromMinutes(35));
            options.AddPolicy(ApiResourcePolicyCatalog.StreamingTimeoutPolicy, TimeSpan.FromMinutes(30));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(CreateFixedWindowPartition),
                PartitionedRateLimiter.Create<HttpContext, string>(CreateConcurrencyPartition));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                }

                if (!context.HttpContext.Response.HasStarted)
                {
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ApiErrorResponse(
                            "请求过于频繁或服务器正在处理较多任务，请稍后重试。",
                            "rate_limited",
                            context.HttpContext.TraceIdentifier),
                        cancellationToken).ConfigureAwait(false);
                }
            };
        });

        return services;
    }

    public static IApplicationBuilder UseExportDocManagerResourceGovernance(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseRateLimiter();
        app.UseRequestTimeouts();
        return app;
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(HttpContext context)
    {
        ApiResourceProfile? profile = ApiResourcePolicyCatalog.Resolve(context);
        if (profile == null)
        {
            return RateLimitPartition.GetNoLimiter("unmanaged");
        }

        ApiResourceLimits limits = ApiResourcePolicyCatalog.GetLimits(profile.Value);
        string clientKey = ResolveClientKey(context);
        string partitionKey = $"{profile.Value}:{clientKey}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = limits.RequestsPerMinute,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            });
    }

    private static RateLimitPartition<string> CreateConcurrencyPartition(HttpContext context)
    {
        ApiResourceProfile? profile = ApiResourcePolicyCatalog.Resolve(context);
        if (profile == null)
        {
            return RateLimitPartition.GetNoLimiter("unmanaged");
        }

        ApiResourceLimits limits = ApiResourcePolicyCatalog.GetLimits(profile.Value);
        return RateLimitPartition.GetConcurrencyLimiter(
            profile.Value.ToString(),
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = limits.ConcurrentRequests,
                QueueLimit = limits.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    }

    private static string ResolveClientKey(HttpContext context)
    {
        IPAddress? address = context.Connection.RemoteIpAddress;
        return address?.MapToIPv6().ToString() ?? "local";
    }
}

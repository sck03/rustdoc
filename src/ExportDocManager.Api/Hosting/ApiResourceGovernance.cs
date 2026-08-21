using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;

namespace ExportDocManager.Api.Hosting;

internal enum ApiResourceProfile
{
    Interactive,
    Login,
    Identity,
    Workload,
    Maintenance,
    Streaming
}

internal sealed record ApiEndpointResourceMetadata(ApiResourceProfile Profile);

internal static class ApiResourcePolicyCatalog
{
    public const string InteractiveTimeoutPolicy = "api-interactive";
    public const string LoginTimeoutPolicy = "api-login";
    public const string IdentityTimeoutPolicy = "api-identity";
    public const string WorkloadTimeoutPolicy = "api-workload";
    public const string MaintenanceTimeoutPolicy = "api-maintenance";
    public const string StreamingTimeoutPolicy = "api-streaming";

    public static string GetTimeoutPolicyName(ApiResourceProfile profile) => profile switch
    {
        ApiResourceProfile.Login => LoginTimeoutPolicy,
        ApiResourceProfile.Identity => IdentityTimeoutPolicy,
        ApiResourceProfile.Workload => WorkloadTimeoutPolicy,
        ApiResourceProfile.Maintenance => MaintenanceTimeoutPolicy,
        ApiResourceProfile.Streaming => StreamingTimeoutPolicy,
        _ => InteractiveTimeoutPolicy
    };

    public static ApiResourceLimits GetLimits(ApiResourceProfile profile) => profile switch
    {
        ApiResourceProfile.Login => new(120, 8, 4),
        ApiResourceProfile.Identity => new(300, 16, 8),
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
            options.AddPolicy(ApiResourcePolicyCatalog.LoginTimeoutPolicy, TimeSpan.FromSeconds(20));
            options.AddPolicy(ApiResourcePolicyCatalog.IdentityTimeoutPolicy, TimeSpan.FromSeconds(30));
            options.AddPolicy(ApiResourcePolicyCatalog.WorkloadTimeoutPolicy, TimeSpan.FromMinutes(5));
            options.AddPolicy(ApiResourcePolicyCatalog.MaintenanceTimeoutPolicy, TimeSpan.FromMinutes(35));
            options.AddPolicy(ApiResourcePolicyCatalog.StreamingTimeoutPolicy, TimeSpan.FromMinutes(30));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(CreateTokenBucketPartition),
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

    private static RateLimitPartition<string> CreateTokenBucketPartition(HttpContext context)
    {
        ApiResourceProfile? profile = ApiResourcePolicyCatalog.Resolve(context);
        if (profile == null)
        {
            return RateLimitPartition.GetNoLimiter("unmanaged");
        }

        ApiResourceLimits limits = ApiResourcePolicyCatalog.GetLimits(profile.Value);
        int tokensPerPeriod = Math.Max(1, limits.RequestsPerMinute / 10);
        string clientKey = ResolveClientKey(context, profile.Value);
        string partitionKey = $"{profile.Value}:{clientKey}";
        return RateLimitPartition.GetTokenBucketLimiter(
            partitionKey,
            _ => new TokenBucketRateLimiterOptions
            {
                AutoReplenishment = true,
                TokenLimit = limits.RequestsPerMinute,
                TokensPerPeriod = tokensPerPeriod,
                QueueLimit = 0,
                ReplenishmentPeriod = TimeSpan.FromMinutes(
                    (double)tokensPerPeriod / limits.RequestsPerMinute)
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
        string clientKey = ResolveClientKey(context, profile.Value);
        return RateLimitPartition.GetConcurrencyLimiter(
            $"{profile.Value}:{clientKey}",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = limits.ConcurrentRequests,
                QueueLimit = limits.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    }

    internal static string ResolveClientKey(HttpContext context, ApiResourceProfile profile)
    {
        if (profile != ApiResourceProfile.Login)
        {
            string token = ApiCurrentUserResolver.GetBearerToken(context);
            if (!string.IsNullOrWhiteSpace(token))
            {
                byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                return "session:" + Convert.ToHexString(digest.AsSpan(0, 12));
            }
        }

        return ResolveAddressKey(context);
    }

    private static string ResolveAddressKey(HttpContext context) =>
        "ip:" + (context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "local");
}

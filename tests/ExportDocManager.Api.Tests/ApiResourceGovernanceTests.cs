using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Api.Tests;

public sealed class ApiResourceGovernanceTests
{
    [Theory]
    [InlineData((int)ApiResourceProfile.Authentication, 30, 6)]
    [InlineData((int)ApiResourceProfile.Interactive, 300, 64)]
    [InlineData((int)ApiResourceProfile.Workload, 90, 12)]
    [InlineData((int)ApiResourceProfile.Maintenance, 20, 2)]
    [InlineData((int)ApiResourceProfile.Streaming, 60, 12)]
    public void ResourceProfiles_ShouldHaveExplicitRateAndConcurrencyBudgets(
        int profileValue,
        int requestsPerMinute,
        int concurrentRequests)
    {
        var profile = (ApiResourceProfile)profileValue;
        ApiResourceLimits limits = ApiResourcePolicyCatalog.GetLimits(profile);

        Assert.Equal(requestsPerMinute, limits.RequestsPerMinute);
        Assert.Equal(concurrentRequests, limits.ConcurrentRequests);
        Assert.False(string.IsNullOrWhiteSpace(ApiResourcePolicyCatalog.GetTimeoutPolicyName(profile)));
    }

    [Fact]
    public void Observability_ShouldKeepOtlpOptionalAndRejectUnsafeEndpoints()
    {
        Assert.Null(ApiObservabilityExtensions.ResolveOptionalOtlpEndpoint(" "));
        Assert.Equal(
            new Uri("https://telemetry.example.test:4317"),
            ApiObservabilityExtensions.ResolveOptionalOtlpEndpoint(
                "https://telemetry.example.test:4317"));
        Assert.Throws<InvalidOperationException>(() =>
            ApiObservabilityExtensions.ResolveOptionalOtlpEndpoint("file:///tmp/collector"));
    }

    [Fact]
    public async Task SecurityAuditMiddleware_ShouldPersistStartedAndCompletedRecordsWithoutRequestPayload()
    {
        string root = Path.Combine(Path.GetTempPath(), $"export-doc-api-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var pathProvider = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            var writer = new ApiSecurityAuditWriter(pathProvider);
            var middleware = new ApiSecurityAuditMiddleware(
                context =>
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return Task.CompletedTask;
                },
                writer,
                NullLogger<ApiSecurityAuditMiddleware>.Instance);
            var context = new DefaultHttpContext();
            context.TraceIdentifier = "audit-correlation";
            context.Request.Method = HttpMethods.Post;
            context.Request.QueryString = new QueryString("?token=must-not-be-audited");
            context.Items[ApiEndpointAuth.AuthenticatedUserItemKey] = new User
            {
                Id = 7,
                Username = "admin"
            };
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/backup/restore"),
                order: 0,
                new EndpointMetadataCollection(
                    new ApiEndpointSecurityAuditMetadata("database-maintenance")),
                displayName: "restore"));

            await middleware.InvokeAsync(context);

            string[] lines = await File.ReadAllLinesAsync(writer.AuditPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"phase\":\"started\"", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"phase\":\"completed\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("/api/backup/restore", lines[1], StringComparison.Ordinal);
            Assert.DoesNotContain("must-not-be-audited", string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

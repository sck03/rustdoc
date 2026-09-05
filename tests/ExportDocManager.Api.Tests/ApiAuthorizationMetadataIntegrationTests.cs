using ExportDocManager.Api.Hosting;
using Microsoft.AspNetCore.Routing;

namespace ExportDocManager.Api.Tests;

public sealed class ApiAuthorizationMetadataIntegrationTests
{
    [Fact]
    public async Task AuthenticatedEndpoints_ShouldDeclareCapabilityLegacyPolicyOrExplicitBypass()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync(
            "authorization-metadata",
            "authorization-metadata.db");

        var missing = harness.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.GetApiAccessMetadata()?.RequiresAuthentication == true)
            .Where(endpoint => endpoint.GetApiCapabilityMetadata() is null)
            .Where(endpoint => endpoint.GetApiPermissionMetadata() is null)
            .Where(endpoint => !endpoint.HasExplicitPermissionBypass())
            .Select(endpoint => endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "<unknown>")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public async Task MigratedBusinessEndpoints_ShouldUseActionCapabilities()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync(
            "business-capability-metadata",
            "business-capability-metadata.db");
        string[] businessPrefixes =
        [
            "/api/crm/",
            "/api/suppliers",
            "/api/email-templates",
            "/api/reports/user-templates",
            "/api/reports/templates"
        ];

        var legacyOnly = harness.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => businessPrefixes.Any(prefix =>
                (endpoint.RoutePattern.RawText ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)))
            .Where(endpoint => endpoint.GetApiCapabilityMetadata() is null)
            .Select(endpoint => endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "<unknown>")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(legacyOnly);
    }
}

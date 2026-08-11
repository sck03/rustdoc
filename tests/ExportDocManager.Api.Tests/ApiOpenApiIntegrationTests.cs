using System.Net;
using System.Text.Json;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiOpenApiIntegrationTests
    {
        [Fact]
        public async Task OfficialOpenApiDocument_ShouldUseMinimalApiEndpointMetadata()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "api-openapi",
                "openapi.db");
            using var client = harness.CreateClient();

            using var response = await client.GetAsync("/openapi/v1.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var paths = document.RootElement.GetProperty("paths");
            Assert.True(paths.TryGetProperty("/api/auth/login", out _));
            Assert.True(paths.TryGetProperty("/api/invoices", out _));
            Assert.False(paths.TryGetProperty("/swagger", out _));

            using var generatedClientDocument = JsonSerializer.SerializeToDocument(
                OpenApiDocumentFactory.Create(new ApiRuntimeOptions()));
            foreach (var documentedPath in generatedClientDocument.RootElement
                         .GetProperty("paths")
                         .EnumerateObject())
            {
                Assert.True(
                    paths.TryGetProperty(documentedPath.Name, out _),
                    $"The generated client still documents an endpoint that Minimal API metadata does not expose: {documentedPath.Name}");
            }
        }
    }
}

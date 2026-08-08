using System.Net;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class WebDavCloudSyncServiceTests
{
    [Fact]
    public async Task SendWithoutRedirectAsync_ShouldRejectRedirectResponses()
    {
        using var client = new HttpClient(new RedirectHandler());
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://dav.example.test/final/backup.zip")
        {
            Content = new ByteArrayContent([1, 2, 3])
        };

        var error = await Assert.ThrowsAsync<ServiceValidationException>(() =>
            WebDavCloudSyncService.SendWithoutRedirectAsync(
                client,
                request,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken.None));

        Assert.Contains("最终的 HTTPS WebDAV 目录", error.Message, StringComparison.Ordinal);
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://other.example.test/webdav/backup.zip") }
            });
        }
    }
}

using System.Net;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiSessionRenewalIntegrationTests
    {
        [Fact]
        public async Task RenewSession_ShouldIssueReplacementAndRevokePreviousToken()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "session-renewal",
                "session-renewal.db");
            using var anonymous = harness.CreateClient();
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var oldSessionClient = harness.CreateClient(login.AccessToken);

            var renewResponse = await oldSessionClient.PostAsync("/api/auth/renew", null);
            Assert.Equal(HttpStatusCode.OK, renewResponse.StatusCode);
            var renewed = await ApiIntegrationTestHarness.ReadJsonAsync<ApiLoginResponse>(renewResponse);
            Assert.NotEqual(login.AccessToken, renewed.AccessToken);
            Assert.True(renewed.ExpiresAt > login.ExpiresAt);

            Assert.Equal(HttpStatusCode.Unauthorized, (await oldSessionClient.GetAsync("/api/auth/me")).StatusCode);
            using var renewedClient = harness.CreateClient(renewed.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await renewedClient.GetAsync("/api/auth/me")).StatusCode);
        }
    }
}

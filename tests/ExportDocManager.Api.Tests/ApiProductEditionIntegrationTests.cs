using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiProductEditionIntegrationTests
    {
        [Theory]
        [InlineData(ProductEditionCatalog.Document, true, false)]
        [InlineData(ProductEditionCatalog.Sales, false, true)]
        [InlineData(ProductEditionCatalog.Full, true, true)]
        public async Task ProductEdition_ShouldGateDocumentAndSalesEndpoints(
            string edition,
            bool documentAllowed,
            bool salesAllowed)
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                $"api-edition-{edition.ToLowerInvariant()}",
                "edition.db",
                productEdition: edition);
            using var anonymous = harness.CreateClient();
            var login = await harness.LoginAsync(anonymous, "admin", string.Empty);
            using var client = harness.CreateClient(login.AccessToken);

            var currentUser = await client.GetFromJsonAsync<ApiUserDto>("/api/auth/me");
            Assert.NotNull(currentUser);
            Assert.Equal(edition, currentUser.Capabilities.ProductEdition);
            Assert.Equal(documentAllowed, currentUser.Capabilities.CanUseDocumentWorkspace);
            Assert.Equal(salesAllowed, currentUser.Capabilities.CanUseSalesWorkspace);
            bool fullEditionAdministrationAllowed = string.Equals(
                edition,
                ProductEditionCatalog.Full,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(fullEditionAdministrationAllowed, currentUser.Capabilities.CanManageUsers);

            var documentResponse = await client.GetAsync("/api/invoices?pageNumber=1&pageSize=5");
            Assert.Equal(documentAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, documentResponse.StatusCode);

            var dashboardResponse = await client.GetAsync("/api/dashboard");
            Assert.Equal(documentAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, dashboardResponse.StatusCode);

            var masterDataResponse = await client.GetAsync("/api/master-data/customers?pageNumber=1&pageSize=5");
            Assert.Equal(documentAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, masterDataResponse.StatusCode);

            var customOptionsResponse = await client.GetAsync("/api/custom-options/Currency");
            Assert.Equal(documentAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, customOptionsResponse.StatusCode);

            var salesResponse = await client.GetAsync("/api/crm/dashboard");
            Assert.Equal(salesAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, salesResponse.StatusCode);

            var emailTemplatesResponse = await client.GetAsync("/api/email-templates");
            Assert.Equal(salesAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, emailTemplatesResponse.StatusCode);

            var usersResponse = await client.GetAsync("/api/users");
            Assert.Equal(fullEditionAdministrationAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, usersResponse.StatusCode);

            var auditLogsResponse = await client.GetAsync("/api/audit-logs?pageNumber=1&pageSize=5");
            Assert.Equal(fullEditionAdministrationAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, auditLogsResponse.StatusCode);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiCustomOptionEndpointIntegrationTests
    {
        [Fact]
        public async Task CustomOptionEndpoints_ShouldSupportLegacyEditableComboBoxOptions()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-custom-options",
                "api-custom-options.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/custom-options/PaymentTerms");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var anonymousSaveResponse = await anonymousClient.PostAsJsonAsync(
                "/api/custom-options/PaymentTerms",
                new { value = "O/A 120" });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousSaveResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var invalidTypeResponse = await adminClient.GetAsync("/api/custom-options/UnknownOption");
            Assert.Equal(HttpStatusCode.BadRequest, invalidTypeResponse.StatusCode);

            var paymentTermsResponse = await adminClient.GetAsync("/api/custom-options/PaymentTerms");
            Assert.Equal(HttpStatusCode.OK, paymentTermsResponse.StatusCode);
            var paymentTerms = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomOptionListResponse>(paymentTermsResponse);
            Assert.Equal("PaymentTerms", paymentTerms.OptionType);
            Assert.True(paymentTerms.AllowCustomValues);
            Assert.Contains("T/T", paymentTerms.PredefinedOptions);
            Assert.Contains("T/T", paymentTerms.Options);
            Assert.Contains("CustomOptions", paymentTerms.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取发票/报关与付款/报销对方数据域", paymentTerms.StoragePolicy, StringComparison.Ordinal);

            var blankSaveResponse = await adminClient.PostAsJsonAsync(
                "/api/custom-options/PaymentTerms",
                new { value = "   " });
            Assert.Equal(HttpStatusCode.BadRequest, blankSaveResponse.StatusCode);

            var saveResponse = await adminClient.PostAsJsonAsync(
                "/api/custom-options/PaymentTerms",
                new { value = " O/A 120 " });
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
            var savedPaymentTerms = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomOptionListResponse>(saveResponse);
            Assert.Contains("O/A 120", savedPaymentTerms.CustomOptions);
            Assert.Contains("O/A 120", savedPaymentTerms.Options);

            var supervisionModeResponse = await adminClient.GetAsync("/api/custom-options/SupervisionMode");
            Assert.Equal(HttpStatusCode.OK, supervisionModeResponse.StatusCode);
            var supervisionModeOptions = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomOptionListResponse>(supervisionModeResponse);
            Assert.Equal("SupervisionMode", supervisionModeOptions.OptionType);
            Assert.True(supervisionModeOptions.AllowCustomValues);
            Assert.Contains("一般贸易", supervisionModeOptions.PredefinedOptions);
            Assert.Contains("一般贸易", supervisionModeOptions.Options);

            var supervisionModeSaveResponse = await adminClient.PostAsJsonAsync(
                "/api/custom-options/SupervisionMode",
                new { value = "特殊监管方式" });
            Assert.Equal(HttpStatusCode.OK, supervisionModeSaveResponse.StatusCode);
            var savedSupervisionModes = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomOptionListResponse>(supervisionModeSaveResponse);
            Assert.Contains("特殊监管方式", savedSupervisionModes.CustomOptions);
            Assert.Contains("特殊监管方式", savedSupervisionModes.Options);

            var duplicateSaveResponse = await adminClient.PostAsJsonAsync(
                "/api/custom-options/PaymentTerms",
                new { value = "o/a 120" });
            Assert.Equal(HttpStatusCode.OK, duplicateSaveResponse.StatusCode);

            await using (var connection = new SqliteConnection($"Data Source={harness.DatabasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT COUNT(*)
                    FROM CustomOptions
                    WHERE OptionType = 'PaymentTerms'
                      AND lower(trim(OptionValue)) = lower('O/A 120')
                    """;
                var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
                Assert.Equal(1L, count);
            }

            var invoiceTypeResponse = await adminClient.GetAsync("/api/custom-options/Type");
            Assert.Equal(HttpStatusCode.OK, invoiceTypeResponse.StatusCode);
            var invoiceTypeOptions = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCustomOptionListResponse>(invoiceTypeResponse);
            Assert.False(invoiceTypeOptions.AllowCustomValues);
            Assert.Contains("实际数据", invoiceTypeOptions.Options);
            Assert.Contains("报关数据", invoiceTypeOptions.Options);

            var customInvoiceTypeResponse = await adminClient.PostAsJsonAsync(
                "/api/custom-options/Type",
                new { value = "其它口径" });
            Assert.Equal(HttpStatusCode.BadRequest, customInvoiceTypeResponse.StatusCode);
        }
    }
}

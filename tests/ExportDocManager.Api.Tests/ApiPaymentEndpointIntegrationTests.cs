using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExportDocManager.Api.Tests
{
    public class ApiPaymentEndpointIntegrationTests
    {
        [Theory]
        [InlineData("permission", HttpStatusCode.Forbidden)]
        [InlineData("infrastructure", HttpStatusCode.ServiceUnavailable)]
        public async Task PaymentCreate_ShouldPreserveClassifiedServiceFailures(
            string failureKind,
            HttpStatusCode expectedStatusCode)
        {
            Exception failure = failureKind switch
            {
                "permission" => new PermissionDeniedException("无权限保存该付款记录。"),
                "infrastructure" => new InfrastructureServiceException("付款数据库暂时不可用。"),
                _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
            };
            var paymentService = new ThrowingPaymentService(failure);
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                $"edm-api-payment-{failureKind}",
                $"api-payment-{failureKind}.db",
                configureServices: services =>
                {
                    services.RemoveAll<IPaymentService>();
                    services.AddSingleton<IPaymentService>(paymentService);
                });
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var response = await adminClient.PostAsJsonAsync("/api/payments", new { });

            Assert.Equal(expectedStatusCode, response.StatusCode);
            Assert.True(paymentService.ReceivedCancellationToken.CanBeCanceled);
        }

        [Fact]
        public async Task PaymentEndpoints_ShouldSupportAuthenticatedCrudAndPersistUnderRuntimeDataRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-payments",
                "api-payments.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/payments");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var blankCreateResponse = await adminClient.PostAsJsonAsync("/api/payments", new { });
            Assert.Equal(HttpStatusCode.Created, blankCreateResponse.StatusCode);
            var blankCreated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentSaveResponse>(blankCreateResponse);
            Assert.Null(blankCreated.Payment.PaymentDate);
            Assert.Equal(string.Empty, blankCreated.Payment.PayeeName);
            Assert.Equal(string.Empty, blankCreated.Payment.PayerName);

            var sealBearingCreateResponse = await adminClient.PostAsJsonAsync("/api/payments", new
            {
                invoiceNo = "PAY-SEAL-REJECT",
                payerName = "Payment Payer",
                paymentDate = new DateTime(2026, 6, 2),
                customsSealPath = "should-never-enter-payment-domain.png"
            });
            Assert.Equal(HttpStatusCode.BadRequest, sealBearingCreateResponse.StatusCode);

            var createResponse = await adminClient.PostAsJsonAsync("/api/payments", new
            {
                invoiceNo = "PAY-API-001",
                shipmentDate = new DateTime(2026, 6, 1),
                payeeId = 0,
                department = "Sales",
                project = "Tauri",
                usdAmount = 100.5m,
                cnyAmount = 720.25m,
                paymentMethod = "TT",
                payeeName = "Factory",
                payerName = "Exporter",
                bankName = "Bank",
                accountNo = "ACC-001",
                notes = "Created from API test",
                paymentDate = new DateTime(2026, 6, 2),
                goodsName = "Coat",
                quantity = "20 PCS",
                shipmentCountry = "US",
                receiptDate = new DateTime(2026, 6, 3),
                travelExpense = 1m,
                businessEntertainmentExpense = 2m,
                telephoneExpense = 3m,
                officeExpense = 4m,
                repairExpense = 5m,
                freightMiscExpense = 6m,
                inspectionExpense = 7m,
                otherExpense = 8m
            });
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentSaveResponse>(createResponse);
            Assert.True(created.Success);
            Assert.True(created.Id > 0);
            Assert.Equal("PAY-API-001", created.Payment.InvoiceNo);
            using (var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()))
            {
                var paymentJson = createDocument.RootElement.GetProperty("payment");
                Assert.False(paymentJson.TryGetProperty("docSealPath", out _));
                Assert.False(paymentJson.TryGetProperty("customsSealPath", out _));
                Assert.False(paymentJson.TryGetProperty("showSeal", out _));
                Assert.False(paymentJson.TryGetProperty("withSeal", out _));
            }

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var getResponse = await adminClient.GetAsync($"/api/payments/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var payment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentDto>(getResponse);
            Assert.Equal("PAY-API-001", payment.InvoiceNo);
            Assert.Equal("Factory", payment.PayeeName);

            var listResponse = await adminClient.GetAsync("/api/payments?pageNumber=1&pageSize=5&keyword=PAY-API");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using (var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
            {
                var root = listDocument.RootElement;
                Assert.True(root.GetProperty("totalCount").GetInt32() >= 1);
                Assert.Contains(
                    root.GetProperty("items").EnumerateArray(),
                    item => string.Equals(
                        item.GetProperty("invoiceNo").GetString(),
                        "PAY-API-001",
                        StringComparison.Ordinal));
            }

            var updateRequest = new
            {
                id = created.Id,
                invoiceNo = "PAY-API-001",
                shipmentDate = new DateTime(2026, 6, 1),
                payeeId = 0,
                department = "Finance",
                project = "Tauri",
                usdAmount = 150m,
                cnyAmount = 1080m,
                paymentMethod = "LC",
                payeeName = "Factory Updated",
                payerName = "Exporter",
                bankName = "Bank",
                accountNo = "ACC-001",
                notes = "Updated from API test",
                paymentDate = new DateTime(2026, 6, 4),
                goodsName = "Coat",
                quantity = "25 PCS",
                shipmentCountry = "US",
                receiptDate = new DateTime(2026, 6, 5),
                travelExpense = 1m,
                businessEntertainmentExpense = 2m,
                telephoneExpense = 3m,
                officeExpense = 4m,
                repairExpense = 5m,
                freightMiscExpense = 6m,
                inspectionExpense = 7m,
                otherExpense = 9m,
                rowVersion = payment.RowVersion
            };
            var updateResponse = await adminClient.PutAsJsonAsync($"/api/payments/{created.Id}", updateRequest);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentSaveResponse>(updateResponse);
            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("Factory Updated", updated.Payment.PayeeName);
            Assert.Equal(150m, updated.Payment.USDAmount);
            var staleUpdateResponse = await adminClient.PutAsJsonAsync(
                $"/api/payments/{created.Id}",
                updateRequest);
            Assert.Equal(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);

            var deleteResponse = await adminClient.DeleteAsync($"/api/payments/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

            var getAfterDeleteResponse = await adminClient.GetAsync($"/api/payments/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
        }

        private sealed class ThrowingPaymentService(Exception failure) : IPaymentService
        {
            public CancellationToken ReceivedCancellationToken { get; private set; }

            public Task<int> SavePaymentAsync(
                Payment payment,
                CancellationToken cancellationToken = default)
            {
                ReceivedCancellationToken = cancellationToken;
                return Task.FromException<int>(failure);
            }

            public Task<bool> DeletePaymentAsync(
                int id,
                CancellationToken cancellationToken = default) =>
                Task.FromException<bool>(failure);
        }
    }
}

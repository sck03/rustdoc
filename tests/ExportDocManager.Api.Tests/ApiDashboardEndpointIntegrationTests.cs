using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using Microsoft.Data.Sqlite;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiDashboardEndpointIntegrationTests
    {
        [Fact]
        public async Task DashboardEndpoint_ShouldReturnAuthenticatedLegacyDashboardSnapshot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-dashboard",
                "api-dashboard.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousResponse = await anonymousClient.GetAsync("/api/dashboard");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            var login = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(login.AccessToken);

            await CreateInvoiceAsync(adminClient, "DASH-001", InvoiceStatusCatalog.Draft, 100m, 56.5m, 20m);
            await CreateInvoiceAsync(adminClient, "DASH-002", InvoiceStatusCatalog.Verified, 200m, 113m, 20m);
            await CreateInvoiceAsync(adminClient, "DASH-003", InvoiceStatusCatalog.Shipped, 300m, 226m, 10m);
            await SetInvoiceDashboardTotalsAsync(harness.DatabasePath, "DASH-001", 53.5m, 10m);
            await SetInvoiceDashboardTotalsAsync(harness.DatabasePath, "DASH-002", 107m, 20m);
            await SetInvoiceDashboardTotalsAsync(harness.DatabasePath, "DASH-003", 94m, 20m);

            var response = await adminClient.GetAsync("/api/dashboard");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dashboard = await ApiIntegrationTestHarness.ReadJsonAsync<ApiDashboardResponse>(response);

            Assert.Equal(600m, dashboard.MonthlyExportAmount);
            Assert.Equal(254.5m, dashboard.MonthlyProfit);
            Assert.Equal(50m, dashboard.MonthlyTaxRefund);
            Assert.Equal(2, dashboard.PendingCount);
            Assert.Equal(1, dashboard.ShippedCount);
            Assert.Equal(3, dashboard.TotalActiveCount);
            Assert.Contains("当前没有待处理批次", dashboard.SingleWindowStatusSummary, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销单据", dashboard.StoragePolicy, StringComparison.Ordinal);

            Assert.Equal(3, dashboard.RecentInvoices.Count);
            Assert.Contains(dashboard.RecentInvoices, invoice =>
                invoice.InvoiceNo == "DASH-003" &&
                invoice.Status == InvoiceStatusCatalog.Shipped &&
                invoice.StatusText == "已出运");
            Assert.Contains(dashboard.TodoItems, item =>
                item.Title.Contains("待收款", StringComparison.Ordinal) &&
                item.Description.Contains("DASH-003", StringComparison.Ordinal) &&
                item.ActionType == "ViewInvoice");
            Assert.Contains(dashboard.TodoItems, item =>
                item.Title.Contains("待出运", StringComparison.Ordinal) &&
                item.Description.Contains("DASH-002", StringComparison.Ordinal));
            Assert.Contains(dashboard.TodoItems, item =>
                item.Title.Contains("待核对", StringComparison.Ordinal) &&
                item.Description.Contains("DASH-001", StringComparison.Ordinal));
        }

        [Fact]
        public async Task DashboardEndpoint_ShouldPreferActualDataWhenSameInvoiceNoHasCustomsData()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-dashboard-invoice-type",
                "api-dashboard-invoice-type.db");
            using var anonymousClient = harness.CreateClient();
            var login = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(login.AccessToken);

            var actual = await CreateInvoiceAsync(
                adminClient,
                "DASH-SAME-001",
                InvoiceStatusCatalog.Verified,
                120m,
                70m,
                13m,
                "实际数据");
            await CreateInvoiceAsync(
                adminClient,
                "DASH-SAME-001",
                InvoiceStatusCatalog.Verified,
                80m,
                50m,
                13m,
                "报关数据");

            var response = await adminClient.GetAsync("/api/dashboard");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dashboard = await ApiIntegrationTestHarness.ReadJsonAsync<ApiDashboardResponse>(response);

            Assert.Equal(120m, dashboard.MonthlyExportAmount);
            Assert.Equal(1, dashboard.TotalActiveCount);
            var recentInvoice = Assert.Single(dashboard.RecentInvoices);
            Assert.Equal(actual.Id, recentInvoice.Id);
            Assert.Equal("实际数据", recentInvoice.Type);
        }

        private static async Task<ApiInvoiceSaveResponse> CreateInvoiceAsync(
            HttpClient client,
            string invoiceNo,
            string status,
            decimal totalAmount,
            decimal purchaseTotal,
            decimal taxRebateRate,
            string type = "Export")
        {
            var invoiceDate = DateTime.Today;
            var response = await client.PostAsJsonAsync(
                "/api/invoices",
                new ApiInvoiceDetailDto
                {
                    InvoiceNo = invoiceNo,
                    ContractNo = $"{invoiceNo}-CON",
                    InvoiceDate = invoiceDate,
                    ShipmentDate = invoiceDate,
                    CustomerNameEN = $"Dashboard Customer {invoiceNo}",
                    CustomerAddressEN = "Dashboard Customer Address",
                    ExporterNameEN = "Dashboard Exporter",
                    ExporterNameCN = "仪表盘出口商",
                    Currency = "USD",
                    Type = type,
                    Status = status,
                    TotalAmount = totalAmount,
                    TotalPurchaseAmount = purchaseTotal,
                    Items =
                    [
                        new ApiInvoiceItemDto
                        {
                            StyleNo = $"{invoiceNo}-STYLE",
                            StyleName = "Dashboard Sample",
                            Quantity = 10m,
                            UnitEN = "PCS",
                            UnitCN = "件",
                            Cartons = 1m,
                            UnitPrice = totalAmount / 10m,
                            TotalPrice = totalAmount,
                            PurchasePrice = purchaseTotal / 10m,
                            PurchaseTotal = purchaseTotal,
                            TaxRebateRate = taxRebateRate
                        }
                    ]
                });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(response);
        }

        private static async Task SetInvoiceDashboardTotalsAsync(
            string databasePath,
            string invoiceNo,
            decimal totalProfit,
            decimal totalTaxRefundAmount)
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Invoices
                SET TotalProfit = $totalProfit,
                    TotalTaxRefundAmount = $totalTaxRefundAmount
                WHERE InvoiceNo = $invoiceNo
                """;
            command.Parameters.AddWithValue("$totalProfit", totalProfit);
            command.Parameters.AddWithValue("$totalTaxRefundAmount", totalTaxRefundAmount);
            command.Parameters.AddWithValue("$invoiceNo", invoiceNo);
            int affected = await command.ExecuteNonQueryAsync();
            Assert.Equal(1, affected);
        }
    }
}

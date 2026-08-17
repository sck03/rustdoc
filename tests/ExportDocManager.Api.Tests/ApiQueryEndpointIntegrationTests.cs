using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiQueryEndpointIntegrationTests
    {
        [Fact]
        public async Task QueryEndpoints_ShouldListAndExportInvoiceRowsWithoutReadingPaymentDomain()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-query",
                "api-query.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/query/invoices");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var anonymousExportResponse = await anonymousClient.PostAsJsonAsync("/api/query/invoices/download", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousExportResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var invoiceResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest("QUERY-INV-001", "实际数据"));
            Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);

            var paymentResponse = await adminClient.PostAsJsonAsync("/api/payments", new ApiPaymentDto
            {
                InvoiceNo = "QUERY-INV-001",
                ShipmentDate = new DateOnly(2026, 6, 20),
                Department = "Query Department",
                Project = "PaymentOnlyMarker",
                USDAmount = 12.34m,
                CNYAmount = 88.88m,
                PaymentMethod = "Wire",
                PayeeName = "PaymentOnlyMarker Payee",
                PayerName = "PaymentOnlyMarker Payer",
                PaymentDate = new DateOnly(2026, 6, 22),
                GoodsName = "PaymentOnlyMarker Goods",
                Quantity = "1",
                ShipmentCountry = "Neverland",
                ReceiptDate = new DateOnly(2026, 6, 23)
            });
            Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);

            var listResponse = await adminClient.GetAsync(
                "/api/query/invoices?startDate=2026-06-01&endDateExclusive=2026-07-01&keyword=Q-STYLE-001&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(listResponse);
            Assert.Equal(1, page.TotalCount);
            var row = Assert.Single(page.Items);
            Assert.Equal("QUERY-INV-001", row.InvoiceNo);
            Assert.Equal("2026-06-01", row.InvoiceDate);
            Assert.Equal("QUERY-INV-001-CON", row.ContractNo);
            Assert.Equal("Query Buyer", row.CustomerName);
            Assert.Equal("Query Exporter", row.ExporterName);
            Assert.Equal("USA", row.DestinationCountry);
            Assert.Equal("FOB", row.TradeTerms);
            Assert.Equal("2026-06-20", row.ShipmentDate);
            Assert.Equal("SEA", row.TransportMode);
            Assert.Equal(120m, row.TotalAmount);

            var dateTimeListResponse = await adminClient.GetAsync(
                "/api/query/invoices?startDate=2026-06-01T00:00:00&endDateExclusive=2026-07-01T00:00:00");
            Assert.Equal(HttpStatusCode.BadRequest, dateTimeListResponse.StatusCode);

            var styleNameKeywordResponse = await adminClient.GetAsync("/api/query/invoices?keyword=Query%20Jacket&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, styleNameKeywordResponse.StatusCode);
            var styleNameKeywordPage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(styleNameKeywordResponse);
            Assert.Equal(1, styleNameKeywordPage.TotalCount);
            Assert.Equal("QUERY-INV-001", Assert.Single(styleNameKeywordPage.Items).InvoiceNo);

            var paymentKeywordResponse = await adminClient.GetAsync("/api/query/invoices?keyword=PaymentOnlyMarker&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, paymentKeywordResponse.StatusCode);
            var paymentKeywordPage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(paymentKeywordResponse);
            Assert.Equal(0, paymentKeywordPage.TotalCount);

            var invalidExportResponse = await adminClient.PostAsJsonAsync("/api/query/invoices/save-to-path", new
            {
                keyword = "QUERY-INV-001",
                destinationPath = Path.Combine(harness.DataRoot, "Exports", "query.txt")
            });
            Assert.Equal(HttpStatusCode.Forbidden, invalidExportResponse.StatusCode);

            var exportResponse = await adminClient.PostAsJsonAsync("/api/query/invoices/download", new
            {
                startDate = new DateOnly(2026, 6, 1),
                endDateExclusive = new DateOnly(2026, 7, 1),
                keyword = "Q-STYLE-001"
            });
            Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);

            using var stringDateContent = new StringContent(
                """{"startDate":"2026-06-01","endDateExclusive":"2026-07-01","keyword":"Q-STYLE-001"}""",
                System.Text.Encoding.UTF8,
                "application/json");
            var stringDateExportResponse = await adminClient.PostAsync("/api/query/invoices/download", stringDateContent);
            Assert.Equal(HttpStatusCode.Accepted, stringDateExportResponse.StatusCode);

            using var dateTimeContent = new StringContent(
                """{"startDate":"2026-06-01T00:00:00","endDateExclusive":"2026-07-01T00:00:00"}""",
                System.Text.Encoding.UTF8,
                "application/json");
            var dateTimeExportResponse = await adminClient.PostAsync("/api/query/invoices/download", dateTimeContent);
            Assert.Equal(HttpStatusCode.BadRequest, dateTimeExportResponse.StatusCode);

            var acceptedJob = await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(exportResponse);
            var completedJob = await WaitForTerminalJobAsync(adminClient, acceptedJob.JobId);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, completedJob.Status);

            var downloadResponse = await adminClient.GetAsync($"/api/jobs/{acceptedJob.JobId}/download");
            Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadResponse.Content.Headers.ContentType?.MediaType);
            await using var exportStream = await downloadResponse.Content.ReadAsStreamAsync();
            using var workbook = new XLWorkbook(exportStream);
            var worksheet = workbook.Worksheet("查询结果");
            Assert.Equal("发票号", worksheet.Cell(1, 1).GetString());
            Assert.Equal("QUERY-INV-001", worksheet.Cell(2, 1).GetString());
            Assert.Equal("2026-06-01", worksheet.Cell(2, 2).GetString());
            Assert.Equal("Query Buyer", worksheet.Cell(2, 4).GetString());
            Assert.Equal("Query Exporter", worksheet.Cell(2, 5).GetString());
            Assert.Equal("USA", worksheet.Cell(2, 6).GetString());

            var emptyExportResponse = await adminClient.PostAsJsonAsync("/api/query/invoices/download", new
            {
                keyword = "NO-SUCH-QUERY-RESULT"
            });
            Assert.Equal(HttpStatusCode.Accepted, emptyExportResponse.StatusCode);
            var emptyAcceptedJob = await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(emptyExportResponse);
            var emptyCompletedJob = await WaitForTerminalJobAsync(adminClient, emptyAcceptedJob.JobId);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, emptyCompletedJob.Status);
            Assert.Contains("仅含表头", emptyCompletedJob.DetailText, StringComparison.Ordinal);

            var emptyDownloadResponse = await adminClient.GetAsync($"/api/jobs/{emptyAcceptedJob.JobId}/download");
            Assert.Equal(HttpStatusCode.OK, emptyDownloadResponse.StatusCode);
            await using var emptyExportStream = await emptyDownloadResponse.Content.ReadAsStreamAsync();
            using var emptyWorkbook = new XLWorkbook(emptyExportStream);
            var emptyWorksheet = emptyWorkbook.Worksheet("查询结果");
            Assert.Equal("发票号", emptyWorksheet.Cell(1, 1).GetString());
            Assert.True(emptyWorksheet.Cell(2, 1).IsEmpty());
        }

        [Fact]
        public async Task QueryEndpoint_ShouldTreatEndDateExclusiveAsAnExclusiveBusinessDate()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-query-exclusive-end",
                "api-query-exclusive-end.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            DateOnly boundary = new(2026, 7, 1);
            var includedResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    "QUERY-BOUNDARY-INCLUDED",
                    "实际数据",
                    shipmentDate: boundary.AddDays(-1)));
            Assert.Equal(HttpStatusCode.Created, includedResponse.StatusCode);

            var excludedResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    "QUERY-BOUNDARY-EXCLUDED",
                    "实际数据",
                    shipmentDate: boundary));
            Assert.Equal(HttpStatusCode.Created, excludedResponse.StatusCode);

            var queryResponse = await adminClient.GetAsync(
                $"/api/query/invoices?startDate=2026-06-30&endDateExclusive={boundary:yyyy-MM-dd}&keyword=QUERY-BOUNDARY&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);

            var page = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(queryResponse);
            var row = Assert.Single(page.Items);
            Assert.Equal("QUERY-BOUNDARY-INCLUDED", row.InvoiceNo);
        }

        private static async Task<BackgroundJobSnapshot> WaitForTerminalJobAsync(HttpClient client, string jobId)
        {
            BackgroundJobSnapshot? lastJob = null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var response = await client.GetAsync($"/api/jobs/{jobId}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var job = await ApiIntegrationTestHarness.ReadJsonAsync<BackgroundJobSnapshot>(response);
                lastJob = job;
                if (BackgroundJobStatusCatalog.IsTerminal(job.Status))
                {
                    return job;
                }

                await Task.Delay(50);
            }

            throw new TimeoutException($"Background job {jobId} did not finish in time. Last status={lastJob?.Status}, text={lastJob?.StatusText}, detail={lastJob?.DetailText}, error={lastJob?.ErrorMessage}.");
        }

        [Fact]
        public async Task InvoiceType_ShouldKeepCustomsAndActualDataIndependentForSameInvoiceNo()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-invoice-type",
                "api-invoice-type.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            const string invoiceNo = "SAME-INV-001";
            var actualCreateResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    invoiceNo,
                    "实际数据",
                    contractNo: "ACTUAL-CON",
                    styleNo: "ACTUAL-STYLE",
                    styleName: "Actual Shipment Goods",
                    totalAmount: 230m));
            Assert.Equal(HttpStatusCode.Created, actualCreateResponse.StatusCode);
            var actualCreate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(actualCreateResponse);
            Assert.False(actualCreate.IsUpdate);

            var customsCreateResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    invoiceNo,
                    "报关数据",
                    contractNo: "CUSTOMS-CON",
                    styleNo: "CUSTOMS-STYLE",
                    styleName: "Customs Declaration Goods",
                    totalAmount: 180m));
            Assert.Equal(HttpStatusCode.Created, customsCreateResponse.StatusCode);
            var customsCreate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(customsCreateResponse);
            Assert.False(customsCreate.IsUpdate);
            Assert.NotEqual(actualCreate.Id, customsCreate.Id);

            var sameNoResponse = await adminClient.GetAsync(
                "/api/query/invoices?keyword=SAME-INV-001&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, sameNoResponse.StatusCode);
            var sameNoPage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(sameNoResponse);
            Assert.Equal(2, sameNoPage.TotalCount);
            Assert.Contains(sameNoPage.Items, item => item.Id == actualCreate.Id && item.Type == "实际数据");
            Assert.Contains(sameNoPage.Items, item => item.Id == customsCreate.Id && item.Type == "报关数据");

            var actualFilterResponse = await adminClient.GetAsync(
                "/api/query/invoices?keyword=SAME-INV-001&invoiceType=%E5%AE%9E%E9%99%85%E6%95%B0%E6%8D%AE&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, actualFilterResponse.StatusCode);
            var actualPage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(actualFilterResponse);
            var actualRow = Assert.Single(actualPage.Items);
            Assert.Equal(actualCreate.Id, actualRow.Id);
            Assert.Equal("实际数据", actualRow.Type);
            Assert.Equal("ACTUAL-CON", actualRow.ContractNo);
            Assert.Equal(230m, actualRow.TotalAmount);

            var customsFilterResponse = await adminClient.GetAsync(
                "/api/query/invoices?keyword=SAME-INV-001&invoiceType=%E6%8A%A5%E5%85%B3%E6%95%B0%E6%8D%AE&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, customsFilterResponse.StatusCode);
            var customsPage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(customsFilterResponse);
            var customsRow = Assert.Single(customsPage.Items);
            Assert.Equal(customsCreate.Id, customsRow.Id);
            Assert.Equal("报关数据", customsRow.Type);
            Assert.Equal("CUSTOMS-CON", customsRow.ContractNo);
            Assert.Equal(180m, customsRow.TotalAmount);

            var actualDetailResponse = await adminClient.GetAsync($"/api/invoices/{actualCreate.Id}");
            Assert.Equal(HttpStatusCode.OK, actualDetailResponse.StatusCode);
            var actualDetail = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceDetailDto>(actualDetailResponse);
            var actualUpdateResponse = await adminClient.PutAsJsonAsync(
                $"/api/invoices/{actualCreate.Id}",
                CreateInvoiceRequest(
                    invoiceNo,
                    "实际数据",
                    id: actualCreate.Id,
                    rowVersion: actualDetail.RowVersion,
                    contractNo: "ACTUAL-CON-UPDATED",
                    styleNo: "ACTUAL-STYLE-UPDATED",
                    styleName: "Updated Actual Shipment Goods",
                    totalAmount: 260m));
            Assert.Equal(HttpStatusCode.OK, actualUpdateResponse.StatusCode);
            var actualUpdate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(actualUpdateResponse);
            Assert.True(actualUpdate.IsUpdate);
            Assert.Equal(actualCreate.Id, actualUpdate.Id);

            var customsAfterUpdateResponse = await adminClient.GetAsync($"/api/invoices/{customsCreate.Id}");
            Assert.Equal(HttpStatusCode.OK, customsAfterUpdateResponse.StatusCode);
            var customsAfterUpdate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceDetailDto>(customsAfterUpdateResponse);
            Assert.Equal("报关数据", customsAfterUpdate.Type);
            Assert.Equal("CUSTOMS-CON", customsAfterUpdate.ContractNo);
            Assert.Equal(180m, customsAfterUpdate.TotalAmount);
            Assert.Equal("CUSTOMS-STYLE", Assert.Single(customsAfterUpdate.Items).StyleNo);
        }

        private static ApiInvoiceDetailDto CreateInvoiceRequest(
            string invoiceNo,
            string type,
            int id = 0,
            string rowVersion = "",
            string contractNo = "",
            string styleNo = "Q-STYLE-001",
            string styleName = "Query Jacket",
            decimal totalAmount = 120m,
            DateOnly? invoiceDate = null,
            DateOnly? shipmentDate = null)
        {
            contractNo = string.IsNullOrWhiteSpace(contractNo)
                ? $"{invoiceNo}-CON"
                : contractNo;

            return new ApiInvoiceDetailDto
            {
                Id = id,
                InvoiceNo = invoiceNo,
                ContractNo = contractNo,
                InvoiceDate = invoiceDate ?? new DateOnly(2026, 6, 1),
                ShipmentDate = shipmentDate ?? new DateOnly(2026, 6, 20),
                CustomerNameEN = "Query Buyer",
                CustomerAddressEN = "1 Query Road",
                ExporterNameEN = "Query Exporter",
                ExporterNameCN = "查询出口商",
                ExporterCreditCode = "91300000000000000X",
                DestinationCountry = "USA",
                TradeTerms = "FOB",
                TransportMode = "SEA",
                Currency = "USD",
                Type = type,
                Status = string.Empty,
                TotalCartons = 2m,
                TotalQuantity = 10m,
                TotalAmount = totalAmount,
                RowVersion = rowVersion,
                Items =
                [
                    new ApiInvoiceItemDto
                    {
                        StyleNo = styleNo,
                        StyleName = styleName,
                        Quantity = 10m,
                        UnitEN = "PCS",
                        Cartons = 2m,
                        UnitPrice = totalAmount / 10m,
                        TotalPrice = totalAmount
                    }
                ]
            };
        }
    }
}

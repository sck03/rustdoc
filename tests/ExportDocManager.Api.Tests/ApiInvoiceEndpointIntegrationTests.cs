using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Api.Tests
{
    public class ApiInvoiceEndpointIntegrationTests
    {
        [Fact]
        public async Task InvoiceEndpoints_ShouldSupportAuthenticatedCrudCloneAndPersistUnderRuntimeDataRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-invoices",
                "api-invoices.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousListResponse = await anonymousClient.GetAsync("/api/invoices");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousListResponse.StatusCode);

            var anonymousProfitResponse = await anonymousClient.PostAsJsonAsync(
                "/api/invoices/profit-analysis",
                new ApiInvoiceProfitAnalysisRequest(CreateInvoiceRequest("INV-PROFIT-DRAFT")));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousProfitResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var profitDraft = CreateInvoiceRequest(
                "INV-PROFIT-DRAFT",
                totalAmount: 100m,
                totalPurchaseAmount: 400m,
                totalTaxRefundAmount: 30m,
                exchangeRate: 7.2m);
            var profitResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices/profit-analysis",
                new ApiInvoiceProfitAnalysisRequest(profitDraft));
            Assert.Equal(HttpStatusCode.OK, profitResponse.StatusCode);
            var profit = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceProfitAnalysisResponse>(profitResponse);
            Assert.Equal("USD", profit.Currency);
            Assert.Equal(100m, profit.SalesTotal);
            Assert.Equal(7.2m, profit.ExchangeRate);
            Assert.Equal(720m, profit.SalesRmb);
            Assert.Equal(400m, profit.PurchaseCost);
            Assert.Equal(30m, profit.TaxRefund);
            Assert.Equal(350m, profit.GrossProfit);
            Assert.Equal(Math.Round(350m / 720m, 28), Math.Round(profit.Margin, 28));
            Assert.Equal("¥ 350.00", profit.GrossProfitText);
            Assert.Contains("不读取付款/报销单据", profit.StoragePolicy, StringComparison.Ordinal);

            var createRequest = CreateInvoiceRequest("INV-API-001");
            var createResponse = await adminClient.PostAsJsonAsync("/api/invoices", createRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(createResponse);
            Assert.True(created.Success);
            Assert.True(created.Id > 0);
            Assert.False(created.IsUpdate);
            Assert.Equal("INV-API-001", created.Invoice.InvoiceNo);

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var getResponse = await adminClient.GetAsync($"/api/invoices/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var invoice = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceDetailDto>(getResponse);
            Assert.Equal("INV-API-001", invoice.InvoiceNo);
            Assert.Equal("API Buyer", invoice.CustomerNameEN);
            Assert.Equal("API Exporter", invoice.ExporterNameEN);
            Assert.Equal("API Broker", invoice.CustomsBrokerName);
            Assert.Equal("1234567890", invoice.CustomsBrokerCode);
            Assert.Equal("API Spare 1", invoice.Spare1);
            Assert.Equal("API Spare 2", invoice.Spare2);
            Assert.Equal("API Spare 3", invoice.Spare3);
            Assert.Equal("{\"legacy\":\"invoice-header\"}", invoice.CustomFieldsJson);
            Assert.Equal("CN Exporter Address", invoice.ExporterAddressCN);
            Assert.Equal("91300000000000000X", invoice.ExporterCreditCode);
            Assert.Equal("API Bank", invoice.BankName);
            Assert.Equal("API-ACCOUNT", invoice.BankAccount);
            Assert.Equal("APISWIFT", invoice.SwiftCode);
            Assert.Equal(7.25m, invoice.ExchangeRate);
            Assert.Equal("Handle with care.", invoice.SpecialTerms);

            var listResponse = await adminClient.GetAsync("/api/invoices?pageNumber=1&pageSize=5&keyword=INV-API-001");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using (var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
            {
                var root = listDocument.RootElement;
                Assert.True(root.GetProperty("totalCount").GetInt32() >= 1);
                Assert.Contains(
                    root.GetProperty("items").EnumerateArray(),
                    item => string.Equals(
                        item.GetProperty("invoiceNo").GetString(),
                        "INV-API-001",
                        StringComparison.Ordinal));
            }

            var updateRequest = CreateInvoiceRequest(
                "INV-API-001",
                created.Id,
                invoice.RowVersion,
                customerName: "API Buyer Updated",
                totalAmount: 250m,
                itemId: invoice.Items.FirstOrDefault()?.Id ?? 0,
                itemInvoiceId: created.Id);
            var updateResponse = await adminClient.PutAsJsonAsync($"/api/invoices/{created.Id}", updateRequest);
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(updateResponse);
            Assert.True(updated.Success);
            Assert.True(updated.IsUpdate);
            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("API Buyer Updated", updated.Invoice.CustomerNameEN);
            Assert.Equal(250m, updated.Invoice.TotalAmount);

            var cloneResponse = await adminClient.PostAsJsonAsync(
                $"/api/invoices/{created.Id}/clone",
                new ApiInvoiceCloneRequest(
                    "INV-API-001-COPY",
                    new InvoiceCloneOptions
                    {
                        CopyHeader = true,
                        CopyItems = true,
                        ResetDates = false,
                        ResetStatus = true,
                        ClearAmounts = false
                    }));
            Assert.Equal(HttpStatusCode.OK, cloneResponse.StatusCode);
            var cloned = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceCloneResponse>(cloneResponse);
            Assert.True(cloned.Success);
            Assert.True(cloned.Id > 0);
            Assert.NotEqual(created.Id, cloned.Id);
            Assert.Equal("INV-API-001-COPY", cloned.Invoice.InvoiceNo);
            Assert.Equal("Draft", cloned.Invoice.Status);
            Assert.Equal(250m, cloned.Invoice.TotalAmount);
            Assert.Single(cloned.Invoice.Items);

            var cloneListResponse = await adminClient.GetAsync("/api/invoices?pageNumber=1&pageSize=5&keyword=INV-API-001-COPY");
            Assert.Equal(HttpStatusCode.OK, cloneListResponse.StatusCode);
            using (var cloneListDocument = JsonDocument.Parse(await cloneListResponse.Content.ReadAsStringAsync()))
            {
                Assert.True(cloneListDocument.RootElement.GetProperty("totalCount").GetInt32() >= 1);
            }

            var deleteResponse = await adminClient.DeleteAsync($"/api/invoices/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

            var getAfterDeleteResponse = await adminClient.GetAsync($"/api/invoices/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
        }

        [Fact]
        public async Task ShippingMarkImageEndpoints_ShouldSaveRuntimeDataRootImageAndRejectOutsidePreviewPath()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-shipping-mark-image",
                "api-shipping-mark-image.db");
            using var anonymousClient = harness.CreateClient();

            const string pngDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=";

            var anonymousSaveResponse = await anonymousClient.PostAsJsonAsync(
                "/api/invoices/shipping-marks/image",
                new ApiShippingMarkImageSaveRequest { ImageDataUrl = pngDataUrl });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousSaveResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var blankSaveResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices/shipping-marks/image",
                new ApiShippingMarkImageSaveRequest { ImageDataUrl = " " });
            Assert.Equal(HttpStatusCode.BadRequest, blankSaveResponse.StatusCode);

            var saveResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices/shipping-marks/image",
                new ApiShippingMarkImageSaveRequest { ImageDataUrl = pngDataUrl });
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
            var saved = await ApiIntegrationTestHarness.ReadJsonAsync<ApiShippingMarkImageSaveResponse>(saveResponse);

            string expectedMarksRoot = Path.Combine(harness.DataRoot, "Marks");
            Assert.Equal("image/png", saved.ContentType);
            Assert.EndsWith(".png", saved.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.True(saved.SizeBytes > 0);
            Assert.StartsWith(expectedMarksRoot, Path.GetFullPath(saved.ImagePath), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(saved.ImagePath));
            Assert.Contains("运行数据根 Marks", saved.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销单据", saved.StoragePolicy, StringComparison.Ordinal);

            var previewResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices/shipping-marks/image/preview",
                new ApiShippingMarkImagePreviewRequest { ImagePath = saved.ImagePath });
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiShippingMarkImagePreviewResponse>(previewResponse);
            Assert.Equal(Path.GetFullPath(saved.ImagePath), Path.GetFullPath(preview.ImagePath));
            Assert.StartsWith("data:image/png;base64,", preview.DataUrl, StringComparison.Ordinal);

            string outsidePath = Path.Combine(harness.DataRoot, "outside-mark.png");
            await File.WriteAllTextAsync(outsidePath, "not a mark");
            var outsidePreviewResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices/shipping-marks/image/preview",
                new ApiShippingMarkImagePreviewRequest { ImagePath = outsidePath });
            Assert.Equal(HttpStatusCode.BadRequest, outsidePreviewResponse.StatusCode);

            var invoiceRequest = CreateInvoiceRequest(
                "INV-MARK-IMAGE-001",
                shippingMarks: string.Empty,
                shippingMarksType: "Image",
                shippingMarksImage: saved.ImagePath);
            var createResponse = await adminClient.PostAsJsonAsync("/api/invoices", invoiceRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(createResponse);
            Assert.Equal("Image", created.Invoice.ShippingMarksType);
            Assert.Equal(saved.ImagePath, created.Invoice.ShippingMarksImage);
            Assert.Equal(string.Empty, created.Invoice.ShippingMarks);
        }

        [Fact]
        public async Task CloneType_ShouldCreateIndependentActualAndCustomsRecordsForSameInvoiceNo()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-invoice-clone-type",
                "api-invoice-clone-type.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createActualResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    "INV-TYPE-CLONE-001",
                    type: "实际数据",
                    totalAmount: 180m));
            Assert.Equal(HttpStatusCode.Created, createActualResponse.StatusCode);
            var actual = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(createActualResponse);

            var cloneResponse = await adminClient.PostAsJsonAsync(
                $"/api/invoices/{actual.Id}/clone-type",
                new ApiInvoiceCloneTypeRequest(
                    "报关数据",
                    new InvoiceCloneOptions
                    {
                        CopyHeader = true,
                        CopyItems = true,
                        ResetDates = false,
                        ResetStatus = true,
                        ClearAmounts = false
                    }));
            Assert.Equal(HttpStatusCode.OK, cloneResponse.StatusCode);
            var customs = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceCloneTypeResponse>(cloneResponse);

            Assert.True(customs.Success);
            Assert.NotEqual(actual.Id, customs.Id);
            Assert.Equal("INV-TYPE-CLONE-001", customs.Invoice.InvoiceNo);
            Assert.Equal("报关数据", customs.Invoice.Type);
            Assert.Equal(180m, customs.Invoice.TotalAmount);
            Assert.Single(customs.Invoice.Items);
            Assert.Equal("API-STYLE", customs.Invoice.Items[0].StyleNo);

            var actualAfterCloneResponse = await adminClient.GetAsync($"/api/invoices/{actual.Id}");
            Assert.Equal(HttpStatusCode.OK, actualAfterCloneResponse.StatusCode);
            var actualAfterClone = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceDetailDto>(actualAfterCloneResponse);
            Assert.Equal("实际数据", actualAfterClone.Type);
            Assert.Equal(180m, actualAfterClone.TotalAmount);

            var duplicateCloneResponse = await adminClient.PostAsJsonAsync(
                $"/api/invoices/{actual.Id}/clone-type",
                new ApiInvoiceCloneTypeRequest("报关数据", new InvoiceCloneOptions()));
            Assert.Equal(HttpStatusCode.Conflict, duplicateCloneResponse.StatusCode);
            var duplicateError = await ApiIntegrationTestHarness.ReadJsonAsync<ApiErrorResponse>(duplicateCloneResponse);
            Assert.Contains("已存在", duplicateError.Message, StringComparison.Ordinal);
            Assert.Contains("未覆盖", duplicateError.Message, StringComparison.Ordinal);

            var sameTypeCloneResponse = await adminClient.PostAsJsonAsync(
                $"/api/invoices/{actual.Id}/clone-type",
                new ApiInvoiceCloneTypeRequest("实际数据", new InvoiceCloneOptions()));
            Assert.Equal(HttpStatusCode.BadRequest, sameTypeCloneResponse.StatusCode);
        }

        [Fact]
        public async Task InvoiceTransferPackageEndpoints_ShouldRoundTripWithoutMergingActualAndCustomsData()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-invoice-transfer-package",
                "api-invoice-transfer-package.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousExportResponse = await anonymousClient.PostAsync(
                "/api/invoices/1/transfer-package/download",
                content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousExportResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var sourceResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    "TRANSFER-SRC-001",
                    type: "实际数据",
                    totalAmount: 180m));
            Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);
            var source = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(sourceResponse);

            var customsResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    "TRANSFER-TARGET-001",
                    type: "报关数据",
                    totalAmount: 90m));
            Assert.Equal(HttpStatusCode.Created, customsResponse.StatusCode);
            var customs = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(customsResponse);

            string packagePath = Path.Combine(harness.DataRoot, "Transfers", "actual-source.edpkg");
            var exportResponse = await adminClient.PostAsync(
                $"/api/invoices/{source.Id}/transfer-package/download",
                content: null);
            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            var packageBytes = await exportResponse.Content.ReadAsByteArrayAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            await File.WriteAllBytesAsync(packagePath, packageBytes);
            Assert.True(File.Exists(packagePath));

            var previewResponse = await adminClient.PostAsync(
                "/api/invoices/transfer-package/upload/preview?fileName=actual-source.edpkg",
                new ByteArrayContent(packageBytes));
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceTransferPreviewResponse>(previewResponse);
            Assert.True(preview.ChecksumValid);
            Assert.Equal("TRANSFER-SRC-001", preview.Preview.InvoiceNo);
            Assert.Equal("实际数据", preview.Preview.Type);
            Assert.True(preview.Preview.InvoiceExists);
            Assert.True(preview.Preview.InvoiceMatches);

            var importResponse = await adminClient.PostAsync(
                "/api/invoices/transfer-package/upload/import?fileName=actual-source.edpkg&conflictAction=NewInvoiceNo&newInvoiceNo=TRANSFER-TARGET-001&allowInvalidChecksum=false",
                new ByteArrayContent(packageBytes));
            Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
            var importResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceTransferImportResponse>(importResponse);
            Assert.True(importResult.Success);
            Assert.Equal("TRANSFER-TARGET-001", importResult.Result.FinalInvoiceNo);
            Assert.Equal("NewInvoiceNo", importResult.Result.ActionTaken);
            Assert.True(importResult.Result.InvoiceId > 0);

            var sameNoResponse = await adminClient.GetAsync(
                "/api/query/invoices?keyword=TRANSFER-TARGET-001&pageNumber=1&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, sameNoResponse.StatusCode);
            var sameNoPage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPagedResponse<ApiQueryInvoiceRowDto>>(sameNoResponse);
            Assert.Equal(2, sameNoPage.TotalCount);
            Assert.Contains(sameNoPage.Items, item => item.Id == customs.Id && item.Type == "报关数据" && item.TotalAmount == 90m);
            Assert.Contains(sameNoPage.Items, item => item.Id == importResult.Result.InvoiceId && item.Type == "实际数据" && item.TotalAmount == 180m);
        }

        [Fact]
        public async Task UnverifyInvoice_ShouldMoveLockedInvoiceBackToDraftWithoutTouchingOtherDomains()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-invoice-unverify",
                "api-invoice-unverify.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousResponse = await anonymousClient.PostAsync("/api/invoices/1/unverify", null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var createResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateInvoiceRequest(
                    "INV-UNVERIFY-001",
                    status: "Verified",
                    type: "实际数据",
                    totalAmount: 320m));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var created = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(createResponse);
            Assert.Equal("Verified", created.Invoice.Status);

            var lockedUpdateResponse = await adminClient.PutAsJsonAsync(
                $"/api/invoices/{created.Id}",
                CreateInvoiceRequest(
                    "INV-UNVERIFY-001",
                    created.Id,
                    created.Invoice.RowVersion,
                    status: "Verified",
                    type: "实际数据",
                    totalAmount: 999m,
                    itemId: created.Invoice.Items.FirstOrDefault()?.Id ?? 0,
                    itemInvoiceId: created.Id));
            Assert.Equal(HttpStatusCode.Conflict, lockedUpdateResponse.StatusCode);
            var lockedUpdateError = await ApiIntegrationTestHarness.ReadJsonAsync<ApiErrorResponse>(lockedUpdateResponse);
            Assert.Contains("反审核", lockedUpdateError.Message, StringComparison.Ordinal);

            var unverifyResponse = await adminClient.PostAsync($"/api/invoices/{created.Id}/unverify", null);
            Assert.Equal(HttpStatusCode.OK, unverifyResponse.StatusCode);
            var unverified = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(unverifyResponse);

            Assert.True(unverified.Success);
            Assert.True(unverified.IsUpdate);
            Assert.Equal(created.Id, unverified.Id);
            Assert.Equal("Draft", unverified.Invoice.Status);
            Assert.Equal("INV-UNVERIFY-001", unverified.Invoice.InvoiceNo);
            Assert.Equal("实际数据", unverified.Invoice.Type);
            Assert.Equal(320m, unverified.Invoice.TotalAmount);
            Assert.Single(unverified.Invoice.Items);

            var getResponse = await adminClient.GetAsync($"/api/invoices/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var persisted = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceDetailDto>(getResponse);
            Assert.Equal("Draft", persisted.Status);

            var draftUpdateResponse = await adminClient.PutAsJsonAsync(
                $"/api/invoices/{created.Id}",
                CreateInvoiceRequest(
                    "INV-UNVERIFY-001",
                    created.Id,
                    persisted.RowVersion,
                    status: "Draft",
                    type: "实际数据",
                    totalAmount: 430m,
                    itemId: persisted.Items.FirstOrDefault()?.Id ?? 0,
                    itemInvoiceId: created.Id));
            Assert.Equal(HttpStatusCode.OK, draftUpdateResponse.StatusCode);
            var draftUpdated = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(draftUpdateResponse);
            Assert.Equal("Draft", draftUpdated.Invoice.Status);
            Assert.Equal(430m, draftUpdated.Invoice.TotalAmount);

            var repeatResponse = await adminClient.PostAsync($"/api/invoices/{created.Id}/unverify", null);
            Assert.Equal(HttpStatusCode.Conflict, repeatResponse.StatusCode);
            var repeatError = await ApiIntegrationTestHarness.ReadJsonAsync<ApiErrorResponse>(repeatResponse);
            Assert.Contains("无需反审核", repeatError.Message, StringComparison.Ordinal);
        }

        private static ApiInvoiceDetailDto CreateInvoiceRequest(
            string invoiceNo,
            int id = 0,
            string rowVersion = "",
            string customerName = "API Buyer",
            decimal totalAmount = 120m,
            decimal totalPurchaseAmount = 0m,
            decimal totalTaxRefundAmount = 0m,
            decimal? exchangeRate = null,
            int itemId = 0,
            int itemInvoiceId = 0,
            string type = "Export",
            string status = "",
            string shippingMarks = "",
            string shippingMarksType = "Text",
            string shippingMarksImage = "")
        {
            return new ApiInvoiceDetailDto
            {
                Id = id,
                InvoiceNo = invoiceNo,
                ContractNo = $"{invoiceNo}-CON",
                InvoiceDate = new DateTime(2026, 6, 1),
                ShipmentDate = new DateTime(2026, 6, 20),
                CustomerNameEN = customerName,
                CustomerAddressEN = "1 API Road",
                ExporterNameEN = "API Exporter",
                ExporterNameCN = "API 出口商",
                ExporterAddressCN = "CN Exporter Address",
                ExporterCreditCode = "91300000000000000X",
                BankName = "API Bank",
                BankAccount = "API-ACCOUNT",
                SwiftCode = "APISWIFT",
                ExchangeRate = exchangeRate ?? 7.25m,
                CustomsBrokerName = "API Broker",
                CustomsBrokerCode = "1234567890",
                Spare1 = "API Spare 1",
                Spare2 = "API Spare 2",
                Spare3 = "API Spare 3",
                CustomFieldsJson = "{\"legacy\":\"invoice-header\"}",
                Currency = "USD",
                Type = type,
                Status = status,
                ShippingMarks = shippingMarks,
                ShippingMarksType = shippingMarksType,
                ShippingMarksImage = shippingMarksImage,
                SpecialTerms = "Handle with care.",
                TotalAmount = totalAmount,
                TotalPurchaseAmount = totalPurchaseAmount,
                TotalTaxRefundAmount = totalTaxRefundAmount,
                RowVersion = rowVersion,
                Items =
                [
                    new ApiInvoiceItemDto
                    {
                        Id = itemId,
                        InvoiceId = itemInvoiceId,
                        StyleNo = "API-STYLE",
                        StyleName = "API Jacket",
                        Quantity = 10m,
                        UnitEN = "PCS",
                        Cartons = 1m,
                        UnitPrice = totalAmount / 10m,
                        TotalPrice = totalAmount
                    }
                ]
            };
        }
    }
}

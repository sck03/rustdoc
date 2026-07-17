using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiReportEndpointIntegrationTests
    {
        [Fact]
        public async Task ReportEndpoints_ShouldRequireAuthenticationAndPreserveValidationBehavior()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-reports",
                "api-reports.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousTemplatesResponse = await anonymousClient.GetAsync("/api/reports/templates");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousTemplatesResponse.StatusCode);

            var anonymousTemplateStorageResponse = await anonymousClient.PostAsync(
                "/api/reports/templates/storage-check",
                null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousTemplateStorageResponse.StatusCode);

            var templatePath = Path.Combine(harness.AppRoot, "Templates", "Export", "designer_test.html");
            var anonymousContentResponse = await anonymousClient.GetAsync(
                $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(templatePath)}");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousContentResponse.StatusCode);

            var anonymousFieldsResponse = await anonymousClient.GetAsync("/api/reports/templates/fields?reportType=ExportDocument");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousFieldsResponse.StatusCode);

            var anonymousDownloadResponse = await anonymousClient.PostAsync("/api/reports/templates/package/download", null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDownloadResponse.StatusCode);

            var anonymousUploadResponse = await anonymousClient.PostAsync(
                "/api/reports/templates/package/upload?strategy=Merge&fileName=anonymous.edtpl",
                new ByteArrayContent([1, 2, 3]));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousUploadResponse.StatusCode);

            var anonymousPaymentPreviewResponse = await anonymousClient.PostAsJsonAsync(
                "/api/reports/payments/1/html-preview",
                new
                {
                    reportType = "PaymentVoucher"
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPaymentPreviewResponse.StatusCode);

            var anonymousPaymentDraftPreviewResponse = await anonymousClient.PostAsJsonAsync(
                "/api/reports/payments/draft/html-preview",
                new
                {
                    reportType = "PaymentVoucher",
                    payment = new
                    {
                        id = 0,
                        invoiceNo = "ANON-DRAFT",
                        payeeName = "Anonymous",
                        payerName = "Anonymous",
                        paymentDate = new DateTime(2026, 6, 2)
                    }
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPaymentDraftPreviewResponse.StatusCode);

            var anonymousPaymentPdfResponse = await anonymousClient.PostAsJsonAsync(
                "/api/reports/payments/1/pdf/download",
                new
                {
                    reportType = "PaymentVoucher",
                    destinationPath = Path.Combine("Reports", "payment.pdf")
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousPaymentPdfResponse.StatusCode);

            var anonymousDocumentPackageResponse = await anonymousClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-package/download",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice",
                            reportType = "ExportDocument",
                            templatePath = "invoice.html",
                            withSeal = true
                        }
                    },
                    includeMergedPdf = true,
                    destinationPath = Path.Combine("Reports", "documents.zip")
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDocumentPackageResponse.StatusCode);

            var anonymousDocumentPackagePreviewResponse = await anonymousClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-package/html-preview",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice",
                            reportType = "ExportDocument",
                            templatePath = "invoice.html",
                            withSeal = true
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDocumentPackagePreviewResponse.StatusCode);

            var anonymousDocumentEmailResponse = await anonymousClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-email",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice",
                            reportType = "ExportDocument",
                            templatePath = "invoice.html",
                            withSeal = true
                        }
                    },
                    includeMergedPdf = false,
                    toAddress = "buyer@example.com",
                    subject = "Documents",
                    body = "Please find the attached documents."
                });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDocumentEmailResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var templateStorageResponse = await adminClient.PostAsync(
                "/api/reports/templates/storage-check",
                null);
            Assert.Equal(HttpStatusCode.OK, templateStorageResponse.StatusCode);
            var templateStorage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateStorageStatusResponse>(templateStorageResponse);
            Assert.True(templateStorage.Exists);
            Assert.True(templateStorage.Writable);
            Assert.Equal(Path.Combine(harness.AppRoot, "Templates"), templateStorage.TemplateRoot);
            Assert.Empty(Directory.GetFiles(templateStorage.TemplateRoot, ".edm-template-write-check-*.tmp"));

            var exportFieldsResponse = await adminClient.GetAsync("/api/reports/templates/fields?reportType=ExportDocument");
            Assert.Equal(HttpStatusCode.OK, exportFieldsResponse.StatusCode);
            var exportFields = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateFieldCatalogResponse>(exportFieldsResponse);
            Assert.Equal("ExportDocument", exportFields.ReportType);
            Assert.Contains("商品明细", exportFields.CategoryOrder);
            Assert.Contains(exportFields.Fields, field => field.Category == "商品明细" && field.Value == "{{ item.StyleNo }}");
            Assert.Contains(exportFields.Fields, field => field.Category == "单据信息" && field.Value == "{{ Invoice.InvoiceNo }}");

            var paymentFieldsResponse = await adminClient.GetAsync("/api/reports/templates/fields?reportType=PaymentVoucher");
            Assert.Equal(HttpStatusCode.OK, paymentFieldsResponse.StatusCode);
            var paymentFields = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateFieldCatalogResponse>(paymentFieldsResponse);
            Assert.Equal("PaymentVoucher", paymentFields.ReportType);
            Assert.Contains("金额换算", paymentFields.CategoryOrder);
            Assert.Contains(paymentFields.Fields, field => field.Category == "金额换算" && field.Value == "{{ cny_amount_upper }}");
            Assert.Contains(paymentFields.Fields, field => field.Category == "付款报销" && field.Value == "{{ Payment.PayeeName }}");
            Assert.Contains(paymentFields.Fields, field => field.Category == "付款报销" && field.Label.Contains("业务参考号", StringComparison.Ordinal));

            var paymentTemplatePath = Path.Combine(harness.AppRoot, "Templates", "Internal", "api_payment_preview.html");
            Directory.CreateDirectory(Path.GetDirectoryName(paymentTemplatePath)!);
            await File.WriteAllTextAsync(
                paymentTemplatePath,
                "<html><body>{{ Payment.InvoiceNo }}|{{ Payment.PayeeName }}|{{ cny_amount_upper }}</body></html>");

            var createPaymentResponse = await adminClient.PostAsJsonAsync("/api/payments", new
            {
                invoiceNo = "PAY-RPT-001",
                shipmentDate = new DateTime(2026, 6, 1),
                payeeId = 0,
                department = "Finance",
                project = "Payment Reports",
                usdAmount = 0m,
                cnyAmount = 1280m,
                paymentMethod = "Bank Transfer",
                payeeName = "Report Payee",
                payerName = "Exporter",
                bankName = "Bank",
                accountNo = "ACC-REPORT",
                notes = "Created from report endpoint test",
                paymentDate = new DateTime(2026, 6, 2),
                goodsName = "Service",
                quantity = "1",
                shipmentCountry = "CN",
                receiptDate = new DateTime(2026, 6, 3),
                travelExpense = 11.11m,
                businessEntertainmentExpense = 0m,
                telephoneExpense = 0m,
                officeExpense = 0m,
                repairExpense = 0m,
                freightMiscExpense = 0m,
                inspectionExpense = 0m,
                otherExpense = 0m
            });
            Assert.Equal(HttpStatusCode.Created, createPaymentResponse.StatusCode);
            var createdPayment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentSaveResponse>(createPaymentResponse);

            var paymentPreviewResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/payments/{createdPayment.Id}/html-preview",
                new
                {
                    reportType = "PaymentVoucher",
                    templatePath = paymentTemplatePath,
                    withSeal = false
                });
            Assert.Equal(HttpStatusCode.OK, paymentPreviewResponse.StatusCode);
            var paymentPreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentReportHtmlPreviewResponse>(paymentPreviewResponse);
            Assert.Equal(createdPayment.Id, paymentPreview.PaymentId);
            Assert.Equal("PaymentVoucher", paymentPreview.ReportType);
            Assert.Contains("PAY-RPT-001", paymentPreview.Html, StringComparison.Ordinal);
            Assert.Contains("Report Payee", paymentPreview.Html, StringComparison.Ordinal);

            var reimbursementTemplatePath = Path.Combine(harness.AppRoot, "Templates", "Internal", "api_expense_reimbursement_preview.html");
            await File.WriteAllTextAsync(
                reimbursementTemplatePath,
                "<html><body>费用报销明细单|{{ Payment.PayerName }}|{{ Payment.Project }}|{{ Payment.CNYAmount | math.format 'F2' }}|{{ Payment.TravelExpense | math.format 'F2' }}</body></html>");
            var reimbursementPreviewResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/payments/{createdPayment.Id}/html-preview",
                new
                {
                    reportType = "PaymentVoucher",
                    templatePath = reimbursementTemplatePath,
                    withSeal = false
                });
            Assert.Equal(HttpStatusCode.OK, reimbursementPreviewResponse.StatusCode);
            var reimbursementPreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentReportHtmlPreviewResponse>(reimbursementPreviewResponse);
            Assert.Equal(createdPayment.Id, reimbursementPreview.PaymentId);
            Assert.Equal("PaymentVoucher", reimbursementPreview.ReportType);
            Assert.Contains("费用报销明细单", reimbursementPreview.Html, StringComparison.Ordinal);
            Assert.Contains("Exporter", reimbursementPreview.Html, StringComparison.Ordinal);
            Assert.Contains("Payment Reports", reimbursementPreview.Html, StringComparison.Ordinal);
            Assert.Contains("1280.00", reimbursementPreview.Html, StringComparison.Ordinal);
            Assert.Contains("11.11", reimbursementPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("Report Payee", reimbursementPreview.Html, StringComparison.Ordinal);

            var templatesResponse = await adminClient.GetAsync("/api/reports/templates?reportType=ExportDocument");
            Assert.Equal(HttpStatusCode.OK, templatesResponse.StatusCode);
            using (var templatesDocument = JsonDocument.Parse(await templatesResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal(JsonValueKind.Array, templatesDocument.RootElement.ValueKind);
            }

            var createTemplateResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/templates",
                new
                {
                    reportType = "ExportDocument",
                    templatePath = "api_created_template",
                    displayName = "API Created Template"
                });
            Assert.Equal(HttpStatusCode.OK, createTemplateResponse.StatusCode);
            var createdTemplate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(createTemplateResponse);
            Assert.Equal("ExportDocument", createdTemplate.ReportType);
            Assert.Equal("API Created Template", createdTemplate.DisplayName);
            Assert.DoesNotContain("EDM_DESIGNER_STATE", createdTemplate.Content, StringComparison.Ordinal);
            Assert.Contains("API Created Template", createdTemplate.Content, StringComparison.Ordinal);
            Assert.EndsWith(".html", createdTemplate.TemplatePath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(createdTemplate.TemplatePath));
            Assert.StartsWith(
                Path.Combine(harness.AppRoot, "Templates", "Export"),
                Path.GetFullPath(createdTemplate.TemplatePath),
                StringComparison.OrdinalIgnoreCase);

            var renamedTemplatePath = Path.Combine(harness.AppRoot, "Templates", "Export", "api_renamed_template.html");
            var renameTemplateResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/templates/rename",
                new
                {
                    reportType = "ExportDocument",
                    templatePath = createdTemplate.TemplatePath,
                    newTemplatePath = renamedTemplatePath
                });
            Assert.Equal(HttpStatusCode.OK, renameTemplateResponse.StatusCode);
            var renamedTemplate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(renameTemplateResponse);
            Assert.Equal(Path.GetFullPath(renamedTemplatePath), renamedTemplate.TemplatePath);
            Assert.False(File.Exists(createdTemplate.TemplatePath));
            Assert.True(File.Exists(renamedTemplatePath));

            var deleteTemplateResponse = await adminClient.DeleteAsync(
                $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(renamedTemplate.TemplatePath)}");
            Assert.Equal(HttpStatusCode.OK, deleteTemplateResponse.StatusCode);
            var deleteTemplateResult = await ApiIntegrationTestHarness.ReadJsonAsync<ApiCommandResponse>(deleteTemplateResponse);
            Assert.True(deleteTemplateResult.Success);
            Assert.False(File.Exists(renamedTemplatePath));

            var templateCatalogPath = Path.Combine(harness.AppRoot, "Templates", "report_templates.json");
            Assert.True(File.Exists(templateCatalogPath));
            Assert.DoesNotContain("api_renamed_template.html", await File.ReadAllTextAsync(templateCatalogPath), StringComparison.OrdinalIgnoreCase);

            var savedHtml = "<!doctype html><html><body>{{ invoice.invoice_no }}</body></html>";
            var saveContentResponse = await adminClient.PutAsJsonAsync(
                "/api/reports/templates/content",
                new
                {
                    reportType = "ExportDocument",
                    templatePath,
                    content = savedHtml
                });
            Assert.Equal(HttpStatusCode.OK, saveContentResponse.StatusCode);
            var savedTemplate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(saveContentResponse);
            Assert.Equal(savedHtml, savedTemplate.Content);
            Assert.Equal(Path.GetFullPath(templatePath), savedTemplate.TemplatePath);
            Assert.Contains("Templates/", savedTemplate.StoragePolicy, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\", savedTemplate.StoragePolicy, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(templatePath));
            Assert.StartsWith(
                Path.Combine(harness.AppRoot, "Templates"),
                Path.GetFullPath(templatePath),
                StringComparison.OrdinalIgnoreCase);

            var getContentResponse = await adminClient.GetAsync(
                $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(templatePath)}");
            Assert.Equal(HttpStatusCode.OK, getContentResponse.StatusCode);
            var loadedTemplate = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplateContentDto>(getContentResponse);
            Assert.Equal(savedHtml, loadedTemplate.Content);

            var previewResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/templates/preview",
                new
                {
                    reportType = "ExportDocument",
                    content = "<html><body>{{ Invoice.InvoiceNo }}</body></html>",
                    withSeal = true
                });
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplatePreviewResponse>(previewResponse);
            Assert.Equal("ExportDocument", preview.ReportType);
            Assert.Contains("PREVIEW-EXPORT-001", preview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("{{", preview.Html, StringComparison.Ordinal);

            var exportPackageResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/templates/package/save-to-path",
                new
                {
                    packagePath = Path.Combine(harness.DataRoot, "TemplatePackages", "api_templates.edtpl")
                });
            Assert.Equal(HttpStatusCode.Forbidden, exportPackageResponse.StatusCode);

            var downloadPackageResponse = await adminClient.PostAsync("/api/reports/templates/package/download", null);
            Assert.Equal(HttpStatusCode.OK, downloadPackageResponse.StatusCode);
            Assert.Equal("application/octet-stream", downloadPackageResponse.Content.Headers.ContentType?.MediaType);
            var downloadedPackageBytes = await downloadPackageResponse.Content.ReadAsByteArrayAsync();
            Assert.True(downloadedPackageBytes.Length > 0);

            File.Delete(templatePath);
            Assert.False(File.Exists(templatePath));
            using (var uploadContent = new ByteArrayContent(downloadedPackageBytes))
            {
                uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var uploadPackageResponse = await adminClient.PostAsync(
                    "/api/reports/templates/package/upload?strategy=Merge&fileName=browser_templates.edtpl",
                    uploadContent);
                Assert.Equal(HttpStatusCode.OK, uploadPackageResponse.StatusCode);
                var uploadedPackage = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportTemplatePackageImportResponse>(uploadPackageResponse);
                Assert.True(uploadedPackage.TemplateCount > 0);
                Assert.Equal("1.0", uploadedPackage.PackageVersion);
                Assert.Contains("Cache/TemplatePackages", uploadedPackage.StoragePolicy, StringComparison.Ordinal);
                Assert.True(File.Exists(templatePath));
            }

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "report-operator",
                fullName = "Report Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "report-operator", "operator-pass");
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);
            var forbiddenStorageCheckResponse = await operatorClient.PostAsync(
                "/api/reports/templates/storage-check",
                null);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenStorageCheckResponse.StatusCode);
            var operatorReadResponse = await operatorClient.GetAsync(
                $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(templatePath)}");
            Assert.Equal(HttpStatusCode.OK, operatorReadResponse.StatusCode);

            var operatorFieldsResponse = await operatorClient.GetAsync("/api/reports/templates/fields?reportType=ExportDocument");
            Assert.Equal(HttpStatusCode.OK, operatorFieldsResponse.StatusCode);

            var forbiddenSaveResponse = await operatorClient.PutAsJsonAsync(
                "/api/reports/templates/content",
                new
                {
                    reportType = "ExportDocument",
                    templatePath,
                    content = "<html>blocked</html>"
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenSaveResponse.StatusCode);
            Assert.Equal(savedHtml, await File.ReadAllTextAsync(templatePath));

            var forbiddenCreateResponse = await operatorClient.PostAsJsonAsync(
                "/api/reports/templates",
                new
                {
                    reportType = "ExportDocument",
                    templatePath = "operator_blocked_template.html",
                    displayName = "Blocked"
                });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreateResponse.StatusCode);
            Assert.False(File.Exists(Path.Combine(harness.AppRoot, "Templates", "Export", "operator_blocked_template.html")));

            var forbiddenDeleteResponse = await operatorClient.DeleteAsync(
                $"/api/reports/templates/content?reportType=ExportDocument&templatePath={Uri.EscapeDataString(templatePath)}");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDeleteResponse.StatusCode);
            Assert.True(File.Exists(templatePath));

            var forbiddenDownloadResponse = await operatorClient.PostAsync("/api/reports/templates/package/download", null);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDownloadResponse.StatusCode);

            using (var forbiddenUploadContent = new ByteArrayContent(downloadedPackageBytes))
            {
                forbiddenUploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var forbiddenUploadResponse = await operatorClient.PostAsync(
                    "/api/reports/templates/package/upload?strategy=Merge&fileName=operator_templates.edtpl",
                    forbiddenUploadContent);
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenUploadResponse.StatusCode);
            }

            var outsideTemplatePath = Path.Combine(harness.DataRoot, "outside-template.html");
            var forbiddenPathResponse = await adminClient.PutAsJsonAsync(
                "/api/reports/templates/content",
                new
                {
                    reportType = "ExportDocument",
                    templatePath = outsideTemplatePath,
                    content = "<html>outside</html>"
                });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenPathResponse.StatusCode);
            Assert.False(File.Exists(outsideTemplatePath));

            var invalidTypeResponse = await adminClient.GetAsync("/api/reports/templates?reportType=NotAReport");
            Assert.Equal(HttpStatusCode.BadRequest, invalidTypeResponse.StatusCode);

            var invalidFieldsTypeResponse = await adminClient.GetAsync("/api/reports/templates/fields?reportType=NotAReport");
            Assert.Equal(HttpStatusCode.BadRequest, invalidFieldsTypeResponse.StatusCode);

            using (var invalidUploadContent = new ByteArrayContent(downloadedPackageBytes))
            {
                invalidUploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var invalidUploadStrategyResponse = await adminClient.PostAsync(
                    "/api/reports/templates/package/upload?strategy=NotAStrategy&fileName=templates.edtpl",
                    invalidUploadContent);
                Assert.Equal(HttpStatusCode.BadRequest, invalidUploadStrategyResponse.StatusCode);
            }

            var invalidContentTypeResponse = await adminClient.GetAsync(
                $"/api/reports/templates/content?reportType=NotAReport&templatePath={Uri.EscapeDataString(templatePath)}");
            Assert.Equal(HttpStatusCode.BadRequest, invalidContentTypeResponse.StatusCode);

            var invalidPreviewResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/0/html-preview",
                new
                {
                    reportType = "ExportDocument"
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidPreviewResponse.StatusCode);

            var invalidPaymentPreviewResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/payments/0/html-preview",
                new
                {
                    reportType = "PaymentVoucher"
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidPaymentPreviewResponse.StatusCode);

            var invalidPaymentPreviewTypeResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/payments/{createdPayment.Id}/html-preview",
                new
                {
                    reportType = "ExportDocument"
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidPaymentPreviewTypeResponse.StatusCode);

            var invalidDocumentPackagePreviewResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-package/html-preview",
                new
                {
                    items = Array.Empty<object>()
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDocumentPackagePreviewResponse.StatusCode);

            var invalidPdfResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/pdf/save-to-path",
                new
                {
                    reportType = "ExportDocument",
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "invoice.txt")
                });
            Assert.Equal(HttpStatusCode.Forbidden, invalidPdfResponse.StatusCode);

            var invalidPaymentPdfResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/payments/{createdPayment.Id}/pdf/save-to-path",
                new
                {
                    reportType = "PaymentVoucher",
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "payment.txt")
                });
            Assert.Equal(HttpStatusCode.Forbidden, invalidPaymentPdfResponse.StatusCode);

            var invalidZipResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/pdf-zip/download",
                new
                {
                    invoiceIds = Array.Empty<int>(),
                    reportType = "ExportDocument",
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "invoices.zip")
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidZipResponse.StatusCode);

            var invalidDocumentPackageInvoiceResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/0/document-package/download",
                new
                {
                    items = Array.Empty<object>(),
                    includeMergedPdf = true,
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "documents.zip")
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDocumentPackageInvoiceResponse.StatusCode);

            var emptyDocumentPackageResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-package/download",
                new
                {
                    items = Array.Empty<object>(),
                    includeMergedPdf = true,
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "documents.zip")
                });
            Assert.Equal(HttpStatusCode.BadRequest, emptyDocumentPackageResponse.StatusCode);

            var invalidDocumentPackageTypeResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-package/download",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Voucher",
                            reportType = "PaymentVoucher",
                            templatePath = templatePath,
                            withSeal = true
                        }
                    },
                    includeMergedPdf = true,
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "documents.zip")
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDocumentPackageTypeResponse.StatusCode);

            var invalidDocumentPackagePathResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-package/save-to-path",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice",
                            reportType = "ExportDocument",
                            templatePath = templatePath,
                            withSeal = true
                        }
                    },
                    includeMergedPdf = true,
                    destinationPath = Path.Combine(harness.DataRoot, "Reports", "documents.txt")
                });
            Assert.Equal(HttpStatusCode.Forbidden, invalidDocumentPackagePathResponse.StatusCode);

            var invalidDocumentEmailInvoiceResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/0/document-email",
                new
                {
                    items = Array.Empty<object>(),
                    includeMergedPdf = false,
                    toAddress = "buyer@example.com",
                    subject = "Documents",
                    body = "Please find the attached documents."
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDocumentEmailInvoiceResponse.StatusCode);

            var emptyDocumentEmailResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-email",
                new
                {
                    items = Array.Empty<object>(),
                    includeMergedPdf = false,
                    toAddress = "buyer@example.com",
                    subject = "Documents",
                    body = "Please find the attached documents."
                });
            Assert.Equal(HttpStatusCode.BadRequest, emptyDocumentEmailResponse.StatusCode);

            var invalidDocumentEmailTypeResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-email",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Voucher",
                            reportType = "PaymentVoucher",
                            templatePath = templatePath,
                            withSeal = true
                        }
                    },
                    includeMergedPdf = false,
                    toAddress = "buyer@example.com",
                    subject = "Documents",
                    body = "Please find the attached documents."
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDocumentEmailTypeResponse.StatusCode);

            var invalidDocumentEmailRecipientResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-email",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice",
                            reportType = "ExportDocument",
                            templatePath = templatePath,
                            withSeal = true
                        }
                    },
                    includeMergedPdf = false,
                    toAddress = "not-an-email-address",
                    subject = "Documents",
                    body = "Please find the attached documents."
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidDocumentEmailRecipientResponse.StatusCode);

            var unconfiguredDocumentEmailResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/1/document-email",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice",
                            reportType = "ExportDocument",
                            templatePath = templatePath,
                            withSeal = true
                        }
                    },
                    includeMergedPdf = false,
                    toAddress = "buyer@example.com",
                    subject = "Documents",
                    body = "Please find the attached documents."
                });
            Assert.Equal(HttpStatusCode.Conflict, unconfiguredDocumentEmailResponse.StatusCode);
        }

        [Fact]
        public async Task ReportHtmlPreviewEndpoints_ShouldKeepInvoiceAndPaymentDataDomainsIndependent()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-report-domain-boundaries",
                "api-report-domain-boundaries.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            const string sharedReference = "BOUNDARY-SAME-001";
            const string invoiceCustomer = "Boundary Invoice Customer";
            const string invoiceContract = "BOUNDARY-CONTRACT-SHOULD-NOT-LEAK";
            const string paymentPayee = "Boundary Payment Payee";
            const string paymentMethod = "BOUNDARY-PAYMENT-METHOD-SHOULD-NOT-LEAK";

            string invoiceTemplatePath = Path.Combine(
                harness.AppRoot,
                "Templates",
                "Export",
                "api_invoice_domain_boundary.html");
            Directory.CreateDirectory(Path.GetDirectoryName(invoiceTemplatePath)!);
            await File.WriteAllTextAsync(
                invoiceTemplatePath,
                "<html><body>{{ Invoice.InvoiceNo }}|{{ Invoice.CustomerNameEN }}|{{ Customer.CustomerNameEN }}|{{ Payment.PayeeName }}|{{ Payment.PaymentMethod }}</body></html>");

            string invoicePackingTemplatePath = Path.Combine(
                harness.AppRoot,
                "Templates",
                "Export",
                "api_invoice_package_boundary.html");
            await File.WriteAllTextAsync(
                invoicePackingTemplatePath,
                "<html><body>PACKAGE|{{ Invoice.InvoiceNo }}|{{ Invoice.ContractNo }}|{{ Payment.PayeeName }}</body></html>");

            string paymentTemplatePath = Path.Combine(
                harness.AppRoot,
                "Templates",
                "Internal",
                "api_payment_domain_boundary.html");
            Directory.CreateDirectory(Path.GetDirectoryName(paymentTemplatePath)!);
            await File.WriteAllTextAsync(
                paymentTemplatePath,
                "<html><body>{{ Payment.InvoiceNo }}|{{ Payment.PayeeName }}|{{ Customer.CustomerNameEN }}|{{ Invoice.ContractNo }}</body></html>");

            var createInvoiceResponse = await adminClient.PostAsJsonAsync(
                "/api/invoices",
                CreateBoundaryInvoiceRequest(sharedReference, invoiceCustomer, invoiceContract));
            Assert.Equal(HttpStatusCode.Created, createInvoiceResponse.StatusCode);
            var createdInvoice = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceSaveResponse>(createInvoiceResponse);

            var createPaymentResponse = await adminClient.PostAsJsonAsync("/api/payments", new
            {
                invoiceNo = sharedReference,
                shipmentDate = new DateTime(2026, 6, 1),
                payeeId = 0,
                department = "Finance",
                project = "Domain Boundary",
                usdAmount = 0m,
                cnyAmount = 66m,
                paymentMethod,
                payeeName = paymentPayee,
                payerName = "Boundary Payer",
                bankName = "Boundary Bank",
                accountNo = "BOUNDARY-ACCOUNT",
                notes = "Payment created for report domain boundary verification.",
                paymentDate = new DateTime(2026, 6, 2),
                goodsName = "Service",
                quantity = "1",
                shipmentCountry = "CN",
                receiptDate = new DateTime(2026, 6, 3),
                travelExpense = 0m,
                businessEntertainmentExpense = 0m,
                telephoneExpense = 0m,
                officeExpense = 0m,
                repairExpense = 0m,
                freightMiscExpense = 0m,
                inspectionExpense = 0m,
                otherExpense = 0m
            });
            Assert.Equal(HttpStatusCode.Created, createPaymentResponse.StatusCode);
            var createdPayment = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentSaveResponse>(createPaymentResponse);

            var invoicePreviewResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/invoices/{createdInvoice.Id}/html-preview",
                new
                {
                    reportType = "ExportDocument",
                    templatePath = invoiceTemplatePath,
                    withSeal = false
                });
            Assert.Equal(HttpStatusCode.OK, invoicePreviewResponse.StatusCode);
            var invoicePreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportHtmlPreviewResponse>(invoicePreviewResponse);
            Assert.Contains(sharedReference, invoicePreview.Html, StringComparison.Ordinal);
            Assert.Contains(invoiceCustomer, invoicePreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(paymentPayee, invoicePreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(paymentMethod, invoicePreview.Html, StringComparison.Ordinal);

            const string draftInvoiceCustomer = "Boundary Draft Invoice Customer";
            const string draftInvoiceContract = "BOUNDARY-DRAFT-CONTRACT";
            var invoiceDraft = CreateBoundaryInvoiceRequest(
                sharedReference,
                draftInvoiceCustomer,
                draftInvoiceContract,
                type: "报关数据");

            var invoiceDraftPreviewResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/invoices/draft/html-preview",
                new
                {
                    reportType = "ExportDocument",
                    templatePath = invoiceTemplatePath,
                    withSeal = false,
                    invoice = invoiceDraft
                });
            Assert.Equal(HttpStatusCode.OK, invoiceDraftPreviewResponse.StatusCode);
            var invoiceDraftPreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiReportHtmlPreviewResponse>(invoiceDraftPreviewResponse);
            Assert.Equal(0, invoiceDraftPreview.InvoiceId);
            Assert.Contains("草稿", invoiceDraftPreview.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains(sharedReference, invoiceDraftPreview.Html, StringComparison.Ordinal);
            Assert.Contains(draftInvoiceCustomer, invoiceDraftPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(invoiceCustomer, invoiceDraftPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(paymentPayee, invoiceDraftPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(paymentMethod, invoiceDraftPreview.Html, StringComparison.Ordinal);

            var packagePreviewResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/invoices/{createdInvoice.Id}/document-package/html-preview",
                new
                {
                    items = new[]
                    {
                        new
                        {
                            name = "Invoice Boundary",
                            reportType = "ExportDocument",
                            templatePath = invoiceTemplatePath,
                            withSeal = false
                        },
                        new
                        {
                            name = "Package Boundary",
                            reportType = "ExportDocument",
                            templatePath = invoicePackingTemplatePath,
                            withSeal = true
                        }
                    }
                });
            Assert.Equal(HttpStatusCode.OK, packagePreviewResponse.StatusCode);
            var packagePreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiInvoiceDocumentPackagePreviewResponse>(packagePreviewResponse);
            Assert.Equal(createdInvoice.Id, packagePreview.InvoiceId);
            Assert.Contains("HTML 预览", packagePreview.StoragePolicy, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\", packagePreview.StoragePolicy, StringComparison.OrdinalIgnoreCase);
            Assert.Collection(
                packagePreview.Items,
                item =>
                {
                    Assert.Equal("Invoice Boundary", item.Name);
                    Assert.False(item.WithSeal);
                    Assert.Contains(sharedReference, item.Html, StringComparison.Ordinal);
                    Assert.Contains(invoiceCustomer, item.Html, StringComparison.Ordinal);
                    Assert.DoesNotContain(paymentPayee, item.Html, StringComparison.Ordinal);
                    Assert.DoesNotContain(paymentMethod, item.Html, StringComparison.Ordinal);
                },
                item =>
                {
                    Assert.Equal("Package Boundary", item.Name);
                    Assert.True(item.WithSeal);
                    Assert.Contains("PACKAGE", item.Html, StringComparison.Ordinal);
                    Assert.Contains(invoiceContract, item.Html, StringComparison.Ordinal);
                    Assert.DoesNotContain(paymentPayee, item.Html, StringComparison.Ordinal);
                });

            var paymentPreviewResponse = await adminClient.PostAsJsonAsync(
                $"/api/reports/payments/{createdPayment.Id}/html-preview",
                new
                {
                    reportType = "PaymentVoucher",
                    templatePath = paymentTemplatePath,
                    withSeal = false
                });
            Assert.Equal(HttpStatusCode.OK, paymentPreviewResponse.StatusCode);
            var paymentPreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentReportHtmlPreviewResponse>(paymentPreviewResponse);
            Assert.Contains(sharedReference, paymentPreview.Html, StringComparison.Ordinal);
            Assert.Contains(paymentPayee, paymentPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(invoiceCustomer, paymentPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(invoiceContract, paymentPreview.Html, StringComparison.Ordinal);

            const string draftPayee = "Boundary Draft Payment Payee";
            var draftPreviewResponse = await adminClient.PostAsJsonAsync(
                "/api/reports/payments/draft/html-preview",
                new
                {
                    reportType = "PaymentVoucher",
                    templatePath = paymentTemplatePath,
                    withSeal = false,
                    payment = new
                    {
                        id = 0,
                        invoiceNo = sharedReference,
                        shipmentDate = new DateTime(2026, 6, 1),
                        payeeId = 0,
                        department = "Finance",
                        project = "Draft Domain Boundary",
                        usdAmount = 0m,
                        cnyAmount = 77m,
                        paymentMethod = "Draft Method",
                        payeeName = draftPayee,
                        payerName = "Draft Payer",
                        bankName = "Draft Bank",
                        accountNo = "DRAFT-ACCOUNT",
                        notes = "Unsaved payment draft preview.",
                        paymentDate = new DateTime(2026, 6, 2),
                        goodsName = "Draft Service",
                        quantity = "1",
                        shipmentCountry = "CN",
                        receiptDate = new DateTime(2026, 6, 3),
                        travelExpense = 0m,
                        businessEntertainmentExpense = 0m,
                        telephoneExpense = 0m,
                        officeExpense = 0m,
                        repairExpense = 0m,
                        freightMiscExpense = 0m,
                        inspectionExpense = 0m,
                        otherExpense = 0m
                    }
                });
            Assert.Equal(HttpStatusCode.OK, draftPreviewResponse.StatusCode);
            var draftPreview = await ApiIntegrationTestHarness.ReadJsonAsync<ApiPaymentReportHtmlPreviewResponse>(draftPreviewResponse);
            Assert.Equal(0, draftPreview.PaymentId);
            Assert.Contains(sharedReference, draftPreview.Html, StringComparison.Ordinal);
            Assert.Contains(draftPayee, draftPreview.Html, StringComparison.Ordinal);
            Assert.Contains("草稿", draftPreview.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不按 Payment.InvoiceNo 读取发票/报关单据", draftPreview.StoragePolicy, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\", draftPreview.StoragePolicy, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(paymentPayee, draftPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(invoiceCustomer, draftPreview.Html, StringComparison.Ordinal);
            Assert.DoesNotContain(invoiceContract, draftPreview.Html, StringComparison.Ordinal);
        }

        private static ApiInvoiceDetailDto CreateBoundaryInvoiceRequest(
            string invoiceNo,
            string customerName,
            string contractNo,
            int id = 0,
            string type = "实际数据")
        {
            return new ApiInvoiceDetailDto
            {
                Id = id,
                InvoiceNo = invoiceNo,
                ContractNo = contractNo,
                InvoiceDate = new DateTime(2026, 6, 1),
                ShipmentDate = new DateTime(2026, 6, 20),
                CustomerNameEN = customerName,
                CustomerAddressEN = "1 Boundary Road",
                ExporterNameEN = "Boundary Exporter",
                ExporterNameCN = "边界出口商",
                ExporterCreditCode = "91300000000000000X",
                Currency = "USD",
                Type = type,
                Status = string.Empty,
                TotalAmount = 10m,
                Items =
                [
                    new ApiInvoiceItemDto
                    {
                        StyleNo = "BOUNDARY-STYLE",
                        StyleName = "Boundary Item",
                        Quantity = 1m,
                        UnitEN = "PCS",
                        Cartons = 1m,
                        UnitPrice = 10m,
                        TotalPrice = 10m
                    }
                ]
            };
        }
    }
}

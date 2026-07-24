using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExportDocManager.Infrastructure.Tests
{
    public class ReportHtmlServiceInfrastructureTests
    {
        [Fact]
        public async Task RenderInvoiceReportAsync_ShouldReadTemplateFromProgramTemplateRoot()
        {
            string appRoot = CreateTempDirectory("report-html-app");
            string dataRoot = CreateTempDirectory("report-html-data");

            try
            {
                string templateDirectory = Path.Combine(appRoot, "Templates", "Export");
                Directory.CreateDirectory(templateDirectory);
                string templatePath = Path.Combine(templateDirectory, "invoice_template.html");
                await File.WriteAllTextAsync(
                    templatePath,
                    "<html><body><h1>{{ Invoice.InvoiceNo }}</h1><p>{{ Customer.CustomerNameEN }}</p></body></html>");

                await using var factory = new TestDbContextFactory();
                int invoiceId = await SeedInvoiceAsync(factory);

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ReportHtmlService(factory, new StubSettingsService(), pathProvider);

                var result = await service.RenderInvoiceReportAsync(
                    invoiceId,
                    ReportDocumentType.ExportDocument,
                    withSeal: false);

                Assert.Equal(ReportDocumentType.ExportDocument, result.ReportType);
                Assert.Equal(invoiceId, result.SourceId);
                Assert.Equal(templatePath, result.TemplatePath);
                Assert.Contains("INV-HTML-001", result.Html, StringComparison.Ordinal);
                Assert.Contains("Acme Trading", result.Html, StringComparison.Ordinal);
                Assert.StartsWith(Path.Combine(appRoot, "Templates"), result.TemplatePath, StringComparison.OrdinalIgnoreCase);
                Assert.False(result.TemplatePath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public async Task RenderPaymentVoucherAsync_ShouldKeepPaymentDataIndependentFromInvoiceData()
        {
            string appRoot = CreateTempDirectory("payment-html-app");
            string dataRoot = CreateTempDirectory("payment-html-data");

            try
            {
                string templateDirectory = Path.Combine(appRoot, "Templates", "Internal");
                Directory.CreateDirectory(templateDirectory);
                string templatePath = Path.Combine(templateDirectory, "payment_voucher_template.html");
                await File.WriteAllTextAsync(
                    templatePath,
                    "<html><body>{{ Payment.InvoiceNo }}|{{ Payee.Name }}|{{ Invoice.ContractNo }}|{{ Customer.CustomerNameEN }}</body></html>");

                await using var factory = new TestDbContextFactory();
                int paymentId = await SeedPaymentWithMatchingInvoiceAsync(factory);

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ReportHtmlService(factory, new StubSettingsService(), pathProvider);

                var result = await service.RenderPaymentVoucherAsync(paymentId, withSeal: false);

                Assert.Equal(ReportDocumentType.PaymentVoucher, result.ReportType);
                Assert.Equal(paymentId, result.SourceId);
                Assert.Equal(templatePath, result.TemplatePath);
                Assert.Contains("PAY-REF-001", result.Html, StringComparison.Ordinal);
                Assert.Contains("Detached Payee", result.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("CT-SHOULD-NOT-LEAK", result.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Customer Should Not Leak", result.Html, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public async Task RenderInvoiceReportAsync_ShouldKeepInvoiceDataIndependentFromPaymentData()
        {
            string appRoot = CreateTempDirectory("invoice-html-app");
            string dataRoot = CreateTempDirectory("invoice-html-data");

            try
            {
                string templateDirectory = Path.Combine(appRoot, "Templates", "Export");
                Directory.CreateDirectory(templateDirectory);
                string templatePath = Path.Combine(templateDirectory, "invoice_template.html");
                await File.WriteAllTextAsync(
                    templatePath,
                    "<html><body>{{ Invoice.InvoiceNo }}|{{ Customer.CustomerNameEN }}|{{ Payment.PaymentMethod }}|{{ Payment.PayeeName }}</body></html>");

                await using var factory = new TestDbContextFactory();
                int invoiceId = await SeedInvoiceWithMatchingPaymentAsync(factory);

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ReportHtmlService(factory, new StubSettingsService(), pathProvider);

                var result = await service.RenderInvoiceReportAsync(
                    invoiceId,
                    ReportDocumentType.ExportDocument,
                    withSeal: false);

                Assert.Equal(ReportDocumentType.ExportDocument, result.ReportType);
                Assert.Equal(invoiceId, result.SourceId);
                Assert.Contains("INV-REF-001", result.Html, StringComparison.Ordinal);
                Assert.Contains("Invoice Customer", result.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("PAYMENT-METHOD-SHOULD-NOT-LEAK", result.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Payee Should Not Leak", result.Html, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public async Task RenderBuiltInProgramTemplates_ShouldRenderFromProgramRootWithoutCrossDomainLeakage()
        {
            string repositoryRoot = FindRepositoryRoot();
            string appRoot = CreateTempDirectory("built-in-report-html-app");
            string dataRoot = CreateTempDirectory("built-in-report-html-data");

            try
            {
                string invoiceTemplatePath = CopyProgramTemplate(repositoryRoot, appRoot, "Export", "invoice_template.html");
                string packingListTemplatePath = CopyProgramTemplate(repositoryRoot, appRoot, "Export", "packing_list_template.html");
                string contractTemplatePath = CopyProgramTemplate(repositoryRoot, appRoot, "Export", "contract_template.html");
                string customsTemplatePath = CopyProgramTemplate(repositoryRoot, appRoot, "Export", "customs_declaration_template.html");
                string paymentVoucherTemplatePath = CopyProgramTemplate(repositoryRoot, appRoot, "Internal", "payment_voucher_template.html");
                string reimbursementTemplatePath = CopyProgramTemplate(repositoryRoot, appRoot, "Internal", "expense_reimbursement_template.html");

                await using var invoiceFactory = new TestDbContextFactory();
                int invoiceId = await SeedInvoiceWithMatchingPaymentAsync(invoiceFactory);
                var invoicePathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var invoiceService = new ReportHtmlService(invoiceFactory, new StubSettingsService(), invoicePathProvider);

                var invoiceResult = await invoiceService.RenderInvoiceReportAsync(
                    invoiceId,
                    ReportDocumentType.ExportDocument,
                    invoiceTemplatePath,
                    withSeal: false);
                AssertRenderedTemplate(invoiceResult, invoiceTemplatePath, appRoot, "INVOICE", "INV-REF-001");
                Assert.DoesNotContain("PAYMENT-METHOD-SHOULD-NOT-LEAK", invoiceResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Payee Should Not Leak", invoiceResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("PO:", invoiceResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Style:", invoiceResult.Html, StringComparison.Ordinal);
                Assert.Contains("PO-I1", invoiceResult.Html, StringComparison.Ordinal);
                Assert.Matches(@">\s*I1\s*<", invoiceResult.Html);

                var packingListResult = await invoiceService.RenderInvoiceReportAsync(
                    invoiceId,
                    ReportDocumentType.ExportDocument,
                    packingListTemplatePath,
                    withSeal: false);
                AssertRenderedTemplate(packingListResult, packingListTemplatePath, appRoot, "PACKING LIST", "INV-REF-001");
                Assert.DoesNotContain("PAYMENT-METHOD-SHOULD-NOT-LEAK", packingListResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Payee Should Not Leak", packingListResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("PO:", packingListResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Style:", packingListResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Order No.:", packingListResult.Html, StringComparison.Ordinal);
                Assert.Contains("PO-I1", packingListResult.Html, StringComparison.Ordinal);
                Assert.Matches(@">\s*I1\s*<", packingListResult.Html);

                var contractResult = await invoiceService.RenderInvoiceReportAsync(
                    invoiceId,
                    ReportDocumentType.ExportDocument,
                    contractTemplatePath,
                    withSeal: false);
                AssertRenderedTemplate(contractResult, contractTemplatePath, appRoot, "售货合同", "CT-INVOICE-001", "Invoice Sample Goods");
                Assert.DoesNotContain("PAYMENT-METHOD-SHOULD-NOT-LEAK", contractResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Payee Should Not Leak", contractResult.Html, StringComparison.Ordinal);

                var customsResult = await invoiceService.RenderInvoiceReportAsync(
                    invoiceId,
                    ReportDocumentType.ExportDocument,
                    customsTemplatePath,
                    withSeal: false);
                AssertRenderedTemplate(customsResult, customsTemplatePath, appRoot, "中华人民共和国海关出口货物报关单", "CT-INVOICE-001", "Invoice Sample Goods");
                Assert.DoesNotContain("PAYMENT-METHOD-SHOULD-NOT-LEAK", customsResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Payee Should Not Leak", customsResult.Html, StringComparison.Ordinal);

                await using var paymentFactory = new TestDbContextFactory();
                int paymentId = await SeedPaymentWithMatchingInvoiceAsync(paymentFactory);
                var paymentPathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var paymentService = new ReportHtmlService(paymentFactory, new StubSettingsService(), paymentPathProvider);

                var paymentVoucherResult = await paymentService.RenderPaymentVoucherAsync(
                    paymentId,
                    paymentVoucherTemplatePath,
                    withSeal: false);
                AssertRenderedTemplate(paymentVoucherResult, paymentVoucherTemplatePath, appRoot, "付款单", "PAY-REF-001");
                Assert.Contains("Detached Payee", paymentVoucherResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("CT-SHOULD-NOT-LEAK", paymentVoucherResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Customer Should Not Leak", paymentVoucherResult.Html, StringComparison.Ordinal);

                var reimbursementResult = await paymentService.RenderPaymentVoucherAsync(
                    paymentId,
                    reimbursementTemplatePath,
                    withSeal: false);
                AssertRenderedTemplate(reimbursementResult, reimbursementTemplatePath, appRoot, "费用报销明细单", "Detached Payer");
                Assert.DoesNotContain("CT-SHOULD-NOT-LEAK", reimbursementResult.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Customer Should Not Leak", reimbursementResult.Html, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void ExportStarterTemplates_ShouldKeepOrderAndStyleFieldsWithoutFixedEnglishPrefixes()
        {
            string invoice = ReportTemplateStarterFactory.Create(
                ReportDocumentType.ExportDocument,
                "INVOICE",
                ReportTemplateStarterFactory.ExportInvoiceStarterPreset);
            string packingList = ReportTemplateStarterFactory.Create(
                ReportDocumentType.ExportDocument,
                "PACKING LIST",
                ReportTemplateStarterFactory.ExportPackingListStarterPreset);

            foreach (string template in new[] { invoice, packingList })
            {
                Assert.Contains("{{ item.PoNumber }}", template, StringComparison.Ordinal);
                Assert.Contains("{{ item.StyleNo }}", template, StringComparison.Ordinal);
                Assert.DoesNotContain("PO:", template, StringComparison.Ordinal);
                Assert.DoesNotContain("Style:", template, StringComparison.Ordinal);
            }

            string repositoryRoot = FindRepositoryRoot();
            string customs = File.ReadAllText(Path.Combine(repositoryRoot, "Templates", "Export", "customs_declaration_template.html"));
            string builtInPackingList = File.ReadAllText(Path.Combine(repositoryRoot, "Templates", "Export", "packing_list_template.html"));
            Assert.Contains("item.StyleNameCN", customs, StringComparison.Ordinal);
            Assert.Contains("customs-item-attributes", customs, StringComparison.Ordinal);

            Assert.Contains("border: 1px solid #111", builtInPackingList, StringComparison.Ordinal);
            Assert.Contains("border-right: 1px solid #111", builtInPackingList, StringComparison.Ordinal);
            Assert.Contains("background: #f2f2f2", builtInPackingList, StringComparison.Ordinal);
            Assert.Contains("text-align: center", builtInPackingList, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RenderBuiltInProgramTemplatesToPdf_ShouldUseProgramRootBrowserAndRuntimeDataRoot()
        {
            string repositoryRoot = FindRepositoryRoot();
            string appRoot = repositoryRoot;
            string dataRoot = CreateTempDirectory("built-in-report-pdf-data");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                Assert.True(ReportFontPolicy.Inspect(pathProvider).Complete, "Pinned Noto CJK report fonts must be provisioned before formal PDF validation.");
                string rendererPath = new ChromiumHtmlToPdfService(pathProvider).ResolveRendererExecutablePath();
                Assert.StartsWith(Path.Combine(appRoot, "Browsers"), rendererPath, StringComparison.OrdinalIgnoreCase);

                await using var invoiceFactory = new TestDbContextFactory();
                int invoiceId = await SeedInvoiceWithMatchingPaymentAsync(invoiceFactory);
                var invoiceHtmlService = new ReportHtmlService(invoiceFactory, new StubSettingsService(), pathProvider);
                var invoicePdfService = new ReportPdfRenderService(invoiceHtmlService, new ChromiumHtmlToPdfService(pathProvider));

                var invoiceTemplates = new[]
                {
                    new BuiltInPdfCase("invoice", ReportDocumentType.ExportDocument, Path.Combine(appRoot, "Templates", "Export", "invoice_template.html"), 1, "portrait", 25000, "portrait"),
                    new BuiltInPdfCase("packing-list", ReportDocumentType.ExportDocument, Path.Combine(appRoot, "Templates", "Export", "packing_list_template.html"), 1, "portrait", 25000, "portrait"),
                    new BuiltInPdfCase("contract", ReportDocumentType.ExportDocument, Path.Combine(appRoot, "Templates", "Export", "contract_template.html"), 1, "portrait", 25000, "portrait"),
                    new BuiltInPdfCase("customs-declaration", ReportDocumentType.ExportDocument, Path.Combine(appRoot, "Templates", "Export", "customs_declaration_template.html"), 1, "landscape", 25000, "landscape")
                };

                foreach (var testCase in invoiceTemplates)
                {
                    var result = await invoicePdfService.RenderInvoicePdfAsync(new ReportPdfRenderRequest
                    {
                        SourceId = invoiceId,
                        ReportType = testCase.ReportType,
                        TemplatePath = testCase.TemplatePath,
                        WithSeal = false,
                        DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", $"{testCase.Slug}.pdf"),
                        DocumentTitle = $"BuiltIn-{testCase.Slug}"
                    });

                    AssertBuiltInPdfResult(result, testCase, appRoot, dataRoot);
                }

                await using var paymentFactory = new TestDbContextFactory();
                int paymentId = await SeedPaymentWithMatchingInvoiceAsync(paymentFactory);
                var paymentHtmlService = new ReportHtmlService(paymentFactory, new StubSettingsService(), pathProvider);
                var paymentPdfService = new ReportPdfRenderService(paymentHtmlService, new ChromiumHtmlToPdfService(pathProvider));
                var paymentTemplates = new[]
                {
                    new BuiltInPdfCase("payment-voucher", ReportDocumentType.PaymentVoucher, Path.Combine(appRoot, "Templates", "Internal", "payment_voucher_template.html"), 1, "portrait", 20000),
                    new BuiltInPdfCase("expense-reimbursement", ReportDocumentType.PaymentVoucher, Path.Combine(appRoot, "Templates", "Internal", "expense_reimbursement_template.html"), 1, "portrait", 20000)
                };

                foreach (var testCase in paymentTemplates)
                {
                    var result = await paymentPdfService.RenderPaymentVoucherPdfAsync(new ReportPdfRenderRequest
                    {
                        SourceId = paymentId,
                        ReportType = testCase.ReportType,
                        TemplatePath = testCase.TemplatePath,
                        WithSeal = false,
                        DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", $"{testCase.Slug}.pdf"),
                        DocumentTitle = $"BuiltIn-{testCase.Slug}"
                    });

                    AssertBuiltInPdfResult(result, testCase, appRoot, dataRoot);
                }
            }
            finally
            {
                if (!ShouldRetainReportTestArtifacts())
                {
                    DeleteDirectoryIfExists(dataRoot);
                }
            }
        }

        [Fact]
        public async Task RenderBuiltInProgramTemplatesWithMultiItemBusinessDataToPdf_ShouldPreservePaginationAndDomainIsolation()
        {
            string repositoryRoot = FindRepositoryRoot();
            string appRoot = repositoryRoot;
            string dataRoot = CreateTempDirectory("built-in-report-multi-page-pdf-data");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                Assert.True(ReportFontPolicy.Inspect(pathProvider).Complete, "Pinned Noto CJK report fonts must be provisioned before cross-platform pagination validation.");
                await using var factory = new TestDbContextFactory();
                var seed = await SeedSameInvoiceNumberActualCustomsAndPaymentAsync(factory);
                var htmlService = new ReportHtmlService(factory, new StubSettingsService(), pathProvider);
                var pdfService = new ReportPdfRenderService(htmlService, new ChromiumHtmlToPdfService(pathProvider));

                string invoiceTemplatePath = Path.Combine(appRoot, "Templates", "Export", "invoice_template.html");
                string packingListTemplatePath = Path.Combine(appRoot, "Templates", "Export", "packing_list_template.html");
                string contractTemplatePath = Path.Combine(appRoot, "Templates", "Export", "contract_template.html");
                string customsTemplatePath = Path.Combine(appRoot, "Templates", "Export", "customs_declaration_template.html");
                string paymentVoucherTemplatePath = Path.Combine(appRoot, "Templates", "Internal", "payment_voucher_template.html");

                var actualInvoiceHtml = await htmlService.RenderInvoiceReportAsync(
                    seed.ActualInvoiceId,
                    ReportDocumentType.ExportDocument,
                    invoiceTemplatePath,
                    withSeal: false);
                Assert.Contains("MULTI-ACTUAL-ITEM-025", actualInvoiceHtml.Html, StringComparison.Ordinal);
                Assert.Contains("Actual Multi Customer", actualInvoiceHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("MULTI-CUSTOMS-ITEM", actualInvoiceHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Customs Multi Customer", actualInvoiceHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("PAYMENT-SAME-NO-SHOULD-NOT-LEAK", actualInvoiceHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain(">0CTN<", actualInvoiceHtml.Html, StringComparison.Ordinal);

                var packingListHtml = await htmlService.RenderInvoiceReportAsync(
                    seed.ActualInvoiceId,
                    ReportDocumentType.ExportDocument,
                    packingListTemplatePath,
                    withSeal: false);
                Assert.Equal(3, Regex.Matches(packingListHtml.Html, "class=\"print-page\"").Count);
                Assert.Contains("24CTN", packingListHtml.Html, StringComparison.Ordinal);
                Assert.Contains("375PCS", packingListHtml.Html, StringComparison.Ordinal);
                Assert.True(
                    Regex.IsMatch(
                        packingListHtml.Html,
                        "MULTI-ACTUAL-ITEM-002.*?<td class=\"numeric-cell package\">\\s*</td>",
                        RegexOptions.Singleline),
                    "Mixed-carton continuation items should keep the package cell empty instead of rendering 0CTN.");

                var customsHtml = await htmlService.RenderInvoiceReportAsync(
                    seed.CustomsInvoiceId,
                    ReportDocumentType.ExportDocument,
                    customsTemplatePath,
                    withSeal: false);
                Assert.Contains("MULTI-CUSTOMS-ITEM-018", customsHtml.Html, StringComparison.Ordinal);
                Assert.Contains("中文品名 001 / 棉制针织男式T恤衫", customsHtml.Html, StringComparison.Ordinal);
                Assert.Contains("Customs Multi Customer", customsHtml.Html, StringComparison.Ordinal);
                Assert.Contains("class=\"customs-item-description\"", customsHtml.Html, StringComparison.Ordinal);
                Assert.Contains("class=\"customs-item-number\"", customsHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("MULTI-ACTUAL-ITEM", customsHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Actual Multi Customer", customsHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("PAYMENT-SAME-NO-SHOULD-NOT-LEAK", customsHtml.Html, StringComparison.Ordinal);

                var paymentHtml = await htmlService.RenderPaymentVoucherAsync(
                    seed.PaymentId,
                    paymentVoucherTemplatePath,
                    withSeal: false);
                Assert.Contains("PAYMENT-SAME-NO-SHOULD-NOT-LEAK", paymentHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("MULTI-ACTUAL-ITEM", paymentHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("MULTI-CUSTOMS-ITEM", paymentHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Actual Multi Customer", paymentHtml.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("Customs Multi Customer", paymentHtml.Html, StringComparison.Ordinal);

                var invoiceResult = await pdfService.RenderInvoicePdfAsync(new ReportPdfRenderRequest
                {
                    SourceId = seed.ActualInvoiceId,
                    ReportType = ReportDocumentType.ExportDocument,
                    TemplatePath = invoiceTemplatePath,
                    WithSeal = false,
                    DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", "multi-item-actual-invoice.pdf"),
                    DocumentTitle = "BuiltIn-MultiItem-Actual-Invoice"
                });
                AssertBuiltInPdfResult(
                    invoiceResult,
                    new BuiltInPdfCase("multi-item-actual-invoice", ReportDocumentType.ExportDocument, invoiceTemplatePath, 3, "portrait", 30000, "portrait"),
                    appRoot,
                    dataRoot);

                var packingListResult = await pdfService.RenderInvoicePdfAsync(new ReportPdfRenderRequest
                {
                    SourceId = seed.ActualInvoiceId,
                    ReportType = ReportDocumentType.ExportDocument,
                    TemplatePath = packingListTemplatePath,
                    WithSeal = false,
                    DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", "multi-item-actual-packing-list.pdf"),
                    DocumentTitle = "BuiltIn-MultiItem-Actual-PackingList"
                });
                AssertBuiltInPdfResult(
                    packingListResult,
                    new BuiltInPdfCase("multi-item-actual-packing-list", ReportDocumentType.ExportDocument, packingListTemplatePath, 3, "portrait", 30000, "portrait"),
                    appRoot,
                    dataRoot);

                var contractResult = await pdfService.RenderInvoicePdfAsync(new ReportPdfRenderRequest
                {
                    SourceId = seed.ActualInvoiceId,
                    ReportType = ReportDocumentType.ExportDocument,
                    TemplatePath = contractTemplatePath,
                    WithSeal = false,
                    DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", "multi-item-actual-contract.pdf"),
                    DocumentTitle = "BuiltIn-MultiItem-Actual-Contract"
                });
                AssertBuiltInPdfResult(
                    contractResult,
                    new BuiltInPdfCase("multi-item-actual-contract", ReportDocumentType.ExportDocument, contractTemplatePath, 2, "portrait", 30000, "portrait"),
                    appRoot,
                    dataRoot);

                var customsResult = await pdfService.RenderInvoicePdfAsync(new ReportPdfRenderRequest
                {
                    SourceId = seed.CustomsInvoiceId,
                    ReportType = ReportDocumentType.ExportDocument,
                    TemplatePath = customsTemplatePath,
                    WithSeal = false,
                    DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", "multi-item-customs-declaration.pdf"),
                    DocumentTitle = "BuiltIn-MultiItem-CustomsDeclaration"
                });
                AssertBuiltInPdfResult(
                    customsResult,
                    new BuiltInPdfCase("multi-item-customs-declaration", ReportDocumentType.ExportDocument, customsTemplatePath, 2, "landscape", 50000, "landscape"),
                    appRoot,
                    dataRoot);

                var paymentResult = await pdfService.RenderPaymentVoucherPdfAsync(new ReportPdfRenderRequest
                {
                    SourceId = seed.PaymentId,
                    ReportType = ReportDocumentType.PaymentVoucher,
                    TemplatePath = paymentVoucherTemplatePath,
                    WithSeal = false,
                    DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", "same-no-payment-voucher.pdf"),
                    DocumentTitle = "BuiltIn-SameNo-PaymentVoucher"
                });
                AssertBuiltInPdfResult(
                    paymentResult,
                    new BuiltInPdfCase("same-no-payment-voucher", ReportDocumentType.PaymentVoucher, paymentVoucherTemplatePath, 1, "portrait", 20000),
                    appRoot,
                    dataRoot);
            }
            finally
            {
                if (!ShouldRetainReportTestArtifacts())
                {
                    DeleteDirectoryIfExists(dataRoot);
                }
            }
        }

        [Fact]
        public async Task ChromiumHtmlToPdfService_WhenRendererMissing_ShouldFailWithoutCreatingDestination()
        {
            string appRoot = CreateTempDirectory("report-pdf-app");
            string dataRoot = CreateTempDirectory("report-pdf-data");
            string destinationPath = Path.Combine(dataRoot, "Exports", "invoice.pdf");
            string originalRendererPath = Environment.GetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, null);
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ChromiumHtmlToPdfService(pathProvider);

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.RenderAsync("<html><body>Report</body></html>", destinationPath));

                Assert.Contains("Browsers", ex.Message, StringComparison.Ordinal);
                Assert.Contains(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, ex.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(destinationPath));
            }
            finally
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, originalRendererPath);
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void ChromiumHtmlToPdfService_ToFileUri_ShouldHandleWindowsDrivePath()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string uri = ChromiumHtmlToPdfService.ToFileUri(@"E:\Users\bridge\Desktop\invoice_template_2024AA001.pdf");

            Assert.Equal("file:///E:/Users/bridge/Desktop/invoice_template_2024AA001.pdf", uri);
        }

        [Fact]
        public void ChromiumHtmlToPdfService_ToFileUri_ShouldHandleWindowsExtendedPathPrefix()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string uri = ChromiumHtmlToPdfService.ToFileUri(@"\\?\E:\rustdoc\ExportDocManager_CS\artifacts\windows-desktop-run\ExportDocManager\Templates\Export\");

            Assert.Equal("file:///E:/rustdoc/ExportDocManager_CS/artifacts/windows-desktop-run/ExportDocManager/Templates/Export/", uri);
        }

        [Fact]
        public void ChromiumHtmlToPdfService_BuildArguments_ShouldOnlyDisableSandboxWhenExplicitlyRequested()
        {
            string htmlPath = Path.Combine(Path.GetTempPath(), "report-source.html");
            string pdfPath = Path.Combine(Path.GetTempPath(), "report-output.pdf");
            string userDataPath = Path.Combine(Path.GetTempPath(), "report-user-data");
            string diskCachePath = Path.Combine(Path.GetTempPath(), "report-disk-cache");

            var sandboxed = ChromiumHtmlToPdfService.BuildChromiumArguments(
                htmlPath,
                pdfPath,
                userDataPath,
                diskCachePath,
                disableSandbox: false);
            var explicitlyUnsandboxed = ChromiumHtmlToPdfService.BuildChromiumArguments(
                htmlPath,
                pdfPath,
                userDataPath,
                diskCachePath,
                disableSandbox: true);

            Assert.DoesNotContain("--no-sandbox", sandboxed);
            Assert.Contains("--no-sandbox", explicitlyUnsandboxed);
            Assert.Equal(sandboxed.Count + 1, explicitlyUnsandboxed.Count);
        }

        [Fact]
        public void ChromiumHtmlToPdfService_WhenHeadlessShellAndChromeExist_ShouldPreferHeadlessShell()
        {
            string appRoot = CreateTempDirectory("report-pdf-app");
            string dataRoot = CreateTempDirectory("report-pdf-data");
            string originalRendererPath = Environment.GetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, null);
                string headlessShellPath = Path.Combine(
                    appRoot,
                    "Browsers",
                    "ChromeForTesting",
                    "win64",
                    "ChromeHeadlessShell",
                    "chrome-headless-shell-win64",
                    "chrome-headless-shell.exe");
                string chromePath = Path.Combine(
                    appRoot,
                    "Browsers",
                    "ChromeForTesting",
                    "win64",
                    "Chrome",
                    "chrome-win64",
                    "chrome.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(headlessShellPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(chromePath)!);
                File.WriteAllText(headlessShellPath, string.Empty);
                File.WriteAllText(chromePath, string.Empty);

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ChromiumHtmlToPdfService(pathProvider);

                string resolvedPath = service.ResolveRendererExecutablePath();

                Assert.Equal(Path.GetFullPath(headlessShellPath), resolvedPath);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, originalRendererPath);
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        private static async Task<int> SeedInvoiceAsync(IDbContextFactory<AppDbContext> factory)
        {
            await using var context = await factory.CreateDbContextAsync();
            var customer = new Customer { CustomerNameEN = "Acme Trading", AddressEN = "1 Main Street" };
            var exporter = new Exporter
            {
                ExporterNameEN = "Bridge Export",
                ExporterNameCN = "桥出口",
                AddressEN = "2 Harbor Road",
                AddressCN = "港口路 2 号",
                CreditCode = "91310000REPORTTEST",
                CustomsCode = "3100123456"
            };
            context.Customers.Add(customer);
            context.Exporters.Add(exporter);
            await context.SaveChangesAsync();

            var invoice = new Invoice
            {
                InvoiceNo = "INV-HTML-001",
                ContractNo = "CT-001",
                InvoiceDate = DateTime.UtcNow.Date,
                ShipmentDate = DateTime.UtcNow.Date,
                CustomerId = customer.Id,
                ExporterId = exporter.Id,
                Currency = "USD",
                ShippingMarks = "N/M",
                ShippingMarksType = "Text",
                TotalAmount = 18.5m,
                Items =
                [
                    new Item
                    {
                        PoNumber = "PO-A1",
                        StyleNo = "A1",
                        StyleName = "Cotton Shirt",
                        FabricComposition = "100% Cotton",
                        UnitEN = "PCS",
                        CtnUnitEN = "CTN",
                        Quantity = 2,
                        Cartons = 1,
                        GWTotal = 1.2m,
                        NWTotal = 1m,
                        Volume = 0.01m,
                        TotalPrice = 18.5m
                    }
                ]
            };

            context.Invoices.Add(invoice);
            await context.SaveChangesAsync();
            return invoice.Id;
        }

        private static async Task<int> SeedPaymentWithMatchingInvoiceAsync(IDbContextFactory<AppDbContext> factory)
        {
            await using var context = await factory.CreateDbContextAsync();
            var customer = new Customer { CustomerNameEN = "Customer Should Not Leak", AddressEN = "1 Hidden Street" };
            var exporter = new Exporter { ExporterNameEN = "Bridge Export", ExporterNameCN = "桥出口", AddressEN = "2 Harbor Road" };
            var payee = new Payee
            {
                Category = "Supplier",
                Name = "Detached Payee",
                BankName = "Detached Bank",
                RMBAccount = "6222000011112222"
            };
            context.Customers.Add(customer);
            context.Exporters.Add(exporter);
            context.Payees.Add(payee);
            await context.SaveChangesAsync();

            context.Invoices.Add(new Invoice
            {
                InvoiceNo = "PAY-REF-001",
                ContractNo = "CT-SHOULD-NOT-LEAK",
                InvoiceDate = DateTime.UtcNow.Date,
                ShipmentDate = DateTime.UtcNow.Date,
                CustomerId = customer.Id,
                ExporterId = exporter.Id,
                Currency = "USD"
            });

            var payment = new Payment
            {
                InvoiceNo = "PAY-REF-001",
                PayeeId = payee.Id,
                PayeeName = payee.Name,
                PayerName = "Detached Payer",
                PaymentDate = DateTime.UtcNow.Date,
                ShipmentDate = DateTime.UtcNow.Date,
                ReceiptDate = DateTime.UtcNow.Date,
                CNYAmount = 1280m,
                USDAmount = 0m,
                Department = "Finance",
                Project = "Detached Project",
                PaymentMethod = "Bank Transfer",
                BankName = payee.BankName,
                AccountNo = payee.RMBAccount,
                Notes = "Detached payment note",
                OtherExpense = 1280m
            };
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
            return payment.Id;
        }

        private static async Task<int> SeedInvoiceWithMatchingPaymentAsync(IDbContextFactory<AppDbContext> factory)
        {
            await using var context = await factory.CreateDbContextAsync();
            var customer = new Customer { CustomerNameEN = "Invoice Customer", AddressEN = "1 Invoice Street" };
            var exporter = new Exporter { ExporterNameEN = "Bridge Export", AddressEN = "2 Harbor Road" };
            context.Customers.Add(customer);
            context.Exporters.Add(exporter);
            await context.SaveChangesAsync();

            var invoice = new Invoice
            {
                InvoiceNo = "INV-REF-001",
                ContractNo = "CT-INVOICE-001",
                InvoiceDate = DateTime.UtcNow.Date,
                ShipmentDate = DateTime.UtcNow.Date,
                CustomerId = customer.Id,
                ExporterId = exporter.Id,
                Currency = "USD",
                ShippingMarks = "N/M",
                ShippingMarksType = "Text",
                PaymentTerms = "T/T",
                PortOfLoading = "SHANGHAI",
                PortOfDestination = "HAMBURG",
                DestinationCountry = "GERMANY",
                TradeTerms = "FOB",
                TransportMode = "BY SEA",
                SupervisionMode = "一般贸易",
                ExporterNameCN = exporter.ExporterNameCN,
                ExporterCreditCode = exporter.CreditCode,
                TotalAmount = 9m,
                TotalCartons = 1,
                TotalQuantity = 1,
                TotalGrossWeight = 1.1m,
                TotalNetWeight = 1m,
                TotalVolume = 0.01m,
                Items =
                [
                    new Item
                    {
                        PoNumber = "PO-I1",
                        StyleNo = "I1",
                        StyleName = "Invoice Sample Goods",
                        FabricComposition = "Polyester",
                        Brand = "Bridge",
                        HSCode = "6205200090",
                        Origin = "CHINA",
                        UnitEN = "PCS",
                        UnitCN = "件",
                        CtnUnitEN = "CTN",
                        CtnUnitCN = "箱",
                        Quantity = 1,
                        Cartons = 1,
                        UnitPrice = 9m,
                        GWTotal = 1.1m,
                        NWTotal = 1m,
                        Volume = 0.01m,
                        TotalPrice = 9m
                    }
                ]
            };
            context.Invoices.Add(invoice);
            context.Payments.Add(new Payment
            {
                InvoiceNo = "INV-REF-001",
                PayeeName = "Payee Should Not Leak",
                PayerName = "Payer",
                PaymentMethod = "PAYMENT-METHOD-SHOULD-NOT-LEAK",
                PaymentDate = DateTime.UtcNow.Date,
                ShipmentDate = DateTime.UtcNow.Date,
                ReceiptDate = DateTime.UtcNow.Date,
                CNYAmount = 300m
            });

            await context.SaveChangesAsync();
            return invoice.Id;
        }

        private static async Task<SameInvoiceNumberSeed> SeedSameInvoiceNumberActualCustomsAndPaymentAsync(IDbContextFactory<AppDbContext> factory)
        {
            await using var context = await factory.CreateDbContextAsync();
            var actualCustomer = new Customer
            {
                CustomerNameEN = "Actual Multi Customer — 上海分公司 / Société Générale International Trading (测试)",
                AddressEN = "ROOM 2801, INTERNATIONAL COMMERCE CENTER, 888 CENTURY AVENUE, PUDONG NEW AREA, SHANGHAI, CHINA № 200120"
            };
            var customsCustomer = new Customer
            {
                CustomerNameEN = "Customs Multi Customer — HONG KONG / 香港进口部 ™",
                AddressEN = "ROOM 1808, COMMERCIAL BUILDING, 128 QUEEN'S ROAD CENTRAL, HONG KONG SAR, CHINA (ATTN: IMPORT DEPT.)"
            };
            var exporter = new Exporter
            {
                ExporterNameEN = "BRIDGE IMPORT & EXPORT CO., LTD. / LONG-NAME CROSS-PLATFORM REPORT VALIDATION",
                ExporterNameCN = "布利杰进出口有限公司（跨平台长名称分页与换行验证）",
                AddressEN = "BUILDING 6, NO. 1888 INTERNATIONAL LOGISTICS AVENUE, NINGBO, ZHEJIANG, CHINA — EXPORT DOCUMENT DEPARTMENT",
                AddressCN = "中国浙江省宁波市国际物流大道1888号6号楼（出口单证部）",
                CreditCode = "91310000MULTIPAGE",
                CustomsCode = "3100999999"
            };
            var payee = new Payee
            {
                Category = "Supplier",
                Name = "Same No Payee",
                BankName = "Same No Bank",
                RMBAccount = "6222000099990000"
            };

            context.Customers.AddRange(actualCustomer, customsCustomer);
            context.Exporters.Add(exporter);
            context.Payees.Add(payee);
            await context.SaveChangesAsync();

            var actualItems = BuildMultiPageItems("MULTI-ACTUAL-ITEM", 25);
            var actualInvoice = BuildMultiPageInvoice(
                "MULTI-SAME-001",
                "实际数据",
                "CT-MULTI-ACTUAL",
                actualCustomer.Id,
                exporter,
                actualItems);

            var customsItems = BuildMultiPageItems("MULTI-CUSTOMS-ITEM", 18);
            var customsInvoice = BuildMultiPageInvoice(
                "MULTI-SAME-001",
                "报关数据",
                "CT-MULTI-CUSTOMS",
                customsCustomer.Id,
                exporter,
                customsItems);

            var payment = new Payment
            {
                InvoiceNo = "MULTI-SAME-001",
                PayeeId = payee.Id,
                PayeeName = payee.Name,
                PayerName = "Same No Payer",
                PaymentDate = new DateTime(2026, 6, 10),
                ShipmentDate = new DateTime(2026, 6, 11),
                ReceiptDate = new DateTime(2026, 6, 12),
                CNYAmount = 4680m,
                USDAmount = 0m,
                Department = "Finance",
                Project = "PAYMENT-SAME-NO-SHOULD-NOT-LEAK",
                PaymentMethod = "电汇",
                BankName = payee.BankName,
                AccountNo = payee.RMBAccount,
                Notes = "Payment record shares the invoice number but remains its own document.",
                OtherExpense = 4680m
            };

            context.Invoices.AddRange(actualInvoice, customsInvoice);
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
            return new SameInvoiceNumberSeed(actualInvoice.Id, customsInvoice.Id, payment.Id);
        }

        private static Invoice BuildMultiPageInvoice(
            string invoiceNo,
            string type,
            string contractNo,
            int customerId,
            Exporter exporter,
            List<Item> items)
        {
            return new Invoice
            {
                InvoiceNo = invoiceNo,
                Type = type,
                ContractNo = contractNo,
                InvoiceDate = new DateTime(2026, 6, 1),
                ShipmentDate = new DateTime(2026, 6, 6),
                CustomerId = customerId,
                ExporterId = exporter.Id,
                Currency = "USD",
                ShippingMarks = $"{type}\nMULTI PAGE MARKS / 唛头测试 ™ № & （） ≤",
                ShippingMarksType = "Text",
                PaymentTerms = "T/T",
                PortOfLoading = "NINGBO",
                PortOfDestination = type == "报关数据" ? "CUSTOMS-PORT" : "ACTUAL-PORT",
                DestinationCountry = type == "报关数据" ? "CUSTOMS-COUNTRY" : "ACTUAL-COUNTRY",
                TradeTerms = "FOB",
                TransportMode = "BY SEA",
                SupervisionMode = "一般贸易",
                ExporterNameCN = exporter.ExporterNameCN,
                ExporterCreditCode = exporter.CreditCode,
                TotalCartons = items.Sum(item => item.Cartons),
                TotalQuantity = items.Sum(item => item.Quantity),
                TotalGrossWeight = items.Sum(item => item.GWTotal),
                TotalNetWeight = items.Sum(item => item.NWTotal),
                TotalVolume = items.Sum(item => item.Volume),
                TotalAmount = items.Sum(item => item.TotalPrice),
                Items = items
            };
        }

        private static List<Item> BuildMultiPageItems(string markerPrefix, int count)
        {
            return Enumerable.Range(1, count)
                .Select(index => new Item
                {
                    PoNumber = $"PO-{index:000}",
                    StyleNo = $"{markerPrefix}-{index:000}",
                    StyleName = $"{markerPrefix}-{index:000} / 棉制针织男式T恤衫 / MEN'S KNITTED T-SHIRT – 100% COTTON ™ № {index:00}",
                    StyleNameCN = $"中文品名 {index:000} / 棉制针织男式T恤衫",
                    FabricComposition = $"100% COTTON / 棉；跨平台换行与特殊符号验证（{markerPrefix}-{index:000}）",
                    Brand = $"Brand{index:00}",
                    HSCode = "6205200090",
                    Origin = "CHINA",
                    UnitEN = "PCS",
                    UnitCN = "件",
                    CtnUnitEN = "CTN",
                    CtnUnitCN = "箱",
                    Quantity = index + 2,
                    Cartons = index == 2 ? 0 : 1,
                    UnitPrice = 3.25m + index,
                    GWTotal = 2.5m + index,
                    NWTotal = 2.0m + index,
                    Volume = 0.08m + index * 0.01m,
                    TotalPrice = (index + 2) * (3.25m + index)
                })
                .ToList();
        }

        private static string CreateTempDirectory(string prefix)
        {
            var path = Path.Combine(
                FindRepositoryRoot(),
                ".codex-runtime",
                "ExportDocManager.Infrastructure.Tests",
                $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CopyProgramTemplate(
            string repositoryRoot,
            string appRoot,
            string category,
            string fileName)
        {
            string sourcePath = Path.Combine(repositoryRoot, "Templates", category, fileName);
            Assert.True(File.Exists(sourcePath), $"内置模板不存在: {sourcePath}");

            string destinationPath = Path.Combine(appRoot, "Templates", category, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return destinationPath;
        }

        private static void AssertRenderedTemplate(
            ReportHtmlRenderResult result,
            string expectedTemplatePath,
            string appRoot,
            params string[] expectedFragments)
        {
            Assert.Equal(expectedTemplatePath, result.TemplatePath);
            Assert.StartsWith(Path.Combine(appRoot, "Templates"), result.TemplatePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("{{", result.Html, StringComparison.Ordinal);
            foreach (string fragment in expectedFragments)
            {
                Assert.Contains(fragment, result.Html, StringComparison.Ordinal);
            }
        }

        private static void AssertBuiltInPdfResult(
            ReportPdfRenderResult result,
            BuiltInPdfCase testCase,
            string appRoot,
            string dataRoot)
        {
            Assert.Equal(testCase.ReportType, result.ReportType);
            Assert.Equal(testCase.TemplatePath, result.TemplatePath);
            Assert.StartsWith(Path.Combine(appRoot, "Templates"), result.TemplatePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.GetFullPath(dataRoot), result.DestinationPath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.Combine(appRoot, "Browsers"), result.RendererPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.DestinationPath), $"PDF was not created: {result.DestinationPath}");
            AssertTemplatePageOrientation(testCase);

            var metrics = AnalyzePdf(result.DestinationPath);
            Assert.True(metrics.Bytes >= testCase.MinBytes, $"{testCase.Slug}: PDF size {metrics.Bytes} below {testCase.MinBytes}.");
            Assert.Equal(testCase.ExpectedPages, metrics.PageCount);
            Assert.Equal(testCase.ExpectedOrientation, metrics.FirstPageOrientation);
            Assert.True(metrics.StreamCount >= testCase.ExpectedPages, $"{testCase.Slug}: expected PDF streams.");

            if (ShouldRetainReportTestArtifacts())
            {
                string metricsPath = Path.ChangeExtension(result.DestinationPath, ".metrics.json");
                File.WriteAllText(
                    metricsPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            testCase.Slug,
                            OperatingSystem = RuntimeInformation.OSDescription,
                            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                            result.RendererPath,
                            metrics.Bytes,
                            metrics.PageCount,
                            metrics.FirstPageOrientation,
                            metrics.StreamCount,
                            metrics.FirstPageWidth,
                            metrics.FirstPageHeight
                        },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private static void AssertTemplatePageOrientation(BuiltInPdfCase testCase)
        {
            if (string.IsNullOrWhiteSpace(testCase.ExpectedTemplatePageOrientation))
            {
                return;
            }

            string templateHtml = File.ReadAllText(testCase.TemplatePath);
            string pattern = $@"@page\s*\{{[^}}]*size\s*:\s*A4\s+{Regex.Escape(testCase.ExpectedTemplatePageOrientation)}\b";
            Assert.Matches(
                new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline),
                templateHtml);
        }

        private static PdfMetrics AnalyzePdf(string pdfPath)
        {
            byte[] bytes = File.ReadAllBytes(pdfPath);
            string content = Encoding.Latin1.GetString(bytes);
            Assert.StartsWith("%PDF-", content, StringComparison.Ordinal);
            Assert.Matches(new Regex(@"%%EOF\s*$", RegexOptions.Singleline), content);

            var mediaBoxMatch = Regex.Match(content, @"/MediaBox\s*\[\s*([^\]]+?)\s*\]");
            Assert.True(mediaBoxMatch.Success, $"{pdfPath}: missing MediaBox.");
            double[] mediaBox = mediaBoxMatch.Groups[1].Value
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => double.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            Assert.True(mediaBox.Length >= 4, $"{pdfPath}: invalid MediaBox.");

            double width = Math.Abs(mediaBox[2] - mediaBox[0]);
            double height = Math.Abs(mediaBox[3] - mediaBox[1]);
            Assert.True(width > 500 && height > 500, $"{pdfPath}: MediaBox is unexpectedly small.");

            return new PdfMetrics(
                Bytes: bytes.Length,
                PageCount: Regex.Matches(content, @"/Type\s*/Page\b").Count,
                FirstPageOrientation: width > height ? "landscape" : "portrait",
                StreamCount: Regex.Matches(content, @"stream(\r\n|\n|\r)").Count,
                FirstPageWidth: width,
                FirstPageHeight: height);
        }

        private static bool ShouldRetainReportTestArtifacts() =>
            string.Equals(
                Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_RETAIN_REPORT_TEST_ARTIFACTS"),
                "1",
                StringComparison.Ordinal);

        private sealed record BuiltInPdfCase(
            string Slug,
            ReportDocumentType ReportType,
            string TemplatePath,
            int ExpectedPages,
            string ExpectedOrientation,
            int MinBytes,
            string ExpectedTemplatePageOrientation = null);

        private sealed record SameInvoiceNumberSeed(
            int ActualInvoiceId,
            int CustomsInvoiceId,
            int PaymentId);

        private sealed record PdfMetrics(
            int Bytes,
            int PageCount,
            string FirstPageOrientation,
            int StreamCount,
            double FirstPageWidth,
            double FirstPageHeight);

        private static string FindRepositoryRoot()
        {
            foreach (string startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(startPath);
                while (directory != null)
                {
                    string markerPath = Path.Combine(
                        directory.FullName,
                        "Templates",
                        "Export",
                        "invoice_template.html");
                    if (File.Exists(markerPath))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException("未找到包含 Templates 的仓库根目录。");
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private sealed class StubSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();

            public Task LoadAsync() => Task.CompletedTask;

            public Task SaveAsync() => Task.CompletedTask;
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IAsyncDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options;

            public TestDbContextFactory()
            {
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .Options;
            }

            public AppDbContext CreateDbContext()
            {
                return new AppDbContext(_options);
            }

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CreateDbContext());
            }

            public async ValueTask DisposeAsync()
            {
                await using var context = CreateDbContext();
                await context.Database.EnsureDeletedAsync();
            }
        }
    }
}


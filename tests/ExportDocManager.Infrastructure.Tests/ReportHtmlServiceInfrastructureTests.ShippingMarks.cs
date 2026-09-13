using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public partial class ReportHtmlServiceInfrastructureTests
{
    [Fact]
    [Trait("Category", BrowserIntegrationCollection.Category)]
    public async Task BuiltInInvoiceAndPackingPdf_UseOneShippingMarkBindingForBothTypes()
    {
        string appRoot = FindRepositoryRoot();
        string dataRoot = CreateTempDirectory("shipping-marks-pdf");
        try
        {
            var paths = new RuntimeAppPathProvider(appRoot, dataRoot);
            var savedImage = await new ShippingMarkImageService(paths).SavePngDataUrlAsync(
                "data:image/png;base64," + Convert.ToBase64String(RasterImageFixtures.Read("png")));
            await using var database = new TestDbContextFactory();
            int invoiceId = await SeedInvoiceWithMatchingPaymentAsync(database);
            var htmlService = CreateService(database, new StubSettingsService(), paths);
            await using var runtime = new BrowserRuntimeManager();
            await using var browser = new ManagedPlaywrightPdfBrowserHost(runtime, new BrowserExecutableResolver(paths), paths);
            var pdfService = new ReportPdfRenderService(htmlService, new ChromiumHtmlToPdfService(paths, runtime, browser));
            foreach (string type in new[] { "Image", "Text" })
            {
                await using (var context = database.CreateDbContext())
                {
                    var invoice = await context.Invoices.SingleAsync(item => item.Id == invoiceId);
                    invoice.ShippingMarksType = type;
                    invoice.ShippingMarksImage = savedImage.ImagePath;
                    invoice.ShippingMarks = "MARK-TEXT & <literal>\nMADE IN CHINA";
                    await context.SaveChangesAsync();
                }
                foreach (string file in new[] { "invoice_template.html", "packing_list_template.html" })
                {
                    string template = Path.Combine(appRoot, "Templates", "Export", file);
                    var html = await htmlService.RenderInvoiceReportAsync(invoiceId, ReportDocumentType.ExportDocument, template, false);
                    if (type == "Image")
                    {
                        Assert.Contains("data:image/png;base64,", html.Html);
                        Assert.DoesNotContain("MARK-TEXT", html.Html);
                    }
                    else
                    {
                        Assert.Contains("MARK-TEXT &amp; &lt;literal&gt;", html.Html);
                        Assert.DoesNotContain("edm-shipping-marks-image", html.Html);
                    }
                    var pdf = await pdfService.RenderInvoicePdfAsync(new ReportPdfRenderRequest
                    {
                        SourceId = invoiceId,
                        ReportType = ReportDocumentType.ExportDocument,
                        TemplatePath = template,
                        WithSeal = false,
                        DestinationPath = Path.Combine(dataRoot, "RenderedPdfs", type + "-" + file + ".pdf")
                    });
                    byte[] bytes = await File.ReadAllBytesAsync(pdf.DestinationPath);
                    Assert.True(bytes.Length > 1000);
                    if (type == "Image") Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(bytes));
                }
            }
            File.Delete(Path.Combine(dataRoot, savedImage.ImagePath.Replace('/', Path.DirectorySeparatorChar)));
            await using (var context = database.CreateDbContext())
            {
                var invoice = await context.Invoices.SingleAsync(item => item.Id == invoiceId);
                invoice.ShippingMarksType = "Image";
                await context.SaveChangesAsync();
            }
            await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() => htmlService.RenderInvoiceReportAsync(
                invoiceId, ReportDocumentType.ExportDocument, Path.Combine(appRoot, "Templates", "Export", "invoice_template.html"), false));
        }
        finally { DeleteDirectoryIfExists(dataRoot); }
    }
}

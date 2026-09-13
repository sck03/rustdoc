using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using HtmlAgilityPack;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ShippingMarksReportingTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "shipping-marks-tests", Guid.NewGuid().ToString("N"));
    private readonly RuntimeAppPathProvider _paths;
    private readonly ShippingMarkImageService _images;

    public ShippingMarksReportingTests()
    {
        _paths = new RuntimeAppPathProvider(Path.Combine(_root, "app"), Path.Combine(_root, "data"));
        Directory.CreateDirectory(_paths.DataRoot);
        _images = new ShippingMarkImageService(_paths);
    }

    [Theory]
    [InlineData("Text")]
    [InlineData(" text ")]
    [InlineData("Image")]
    [InlineData(" image ")]
    public async Task OneBinding_RendersSelectedContentInEveryContainerWithoutChangingInvoice(string type)
    {
        var image = await SaveImageAsync();
        var invoice = new Invoice
        {
            InvoiceNo = "MARK-1",
            ShippingMarksType = type,
            ShippingMarksImage = image.ImagePath,
            ShippingMarks = "N/M <script>do not execute</script> & 唛头\nMADE IN CHINA"
        };
        const string template = "<div>{{ Invoice.ShippingMarks }}</div><table><tr><td>{{ Invoice.ShippingMarks }}</td></tr></table><aside>{{ Invoice.ShippingMarks }}</aside>";
        var globals = await ReportTemplateGlobalsBuilder.BuildInvoiceGlobalsAsync(invoice, null, null, false, _images);
        var html = ScribanReportTemplateRenderer.Render(template, globals);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        Assert.Equal("MARK-1", ScribanReportTemplateRenderer.Render("{{ Invoice.InvoiceNo }}", globals));
        Assert.False(globals.ContainsKey("shipping_marks_image_data"));
        Assert.Empty(document.DocumentNode.Descendants("script"));
        if (ShippingMarksTypeCatalog.Normalize(type) == ShippingMarksTypeCatalog.Image)
        {
            var nodes = document.DocumentNode.Descendants("img").ToArray();
            Assert.Equal(3, nodes.Length);
            Assert.All(nodes, node => Assert.StartsWith("data:image/png;base64,", node.GetAttributeValue("src", "")));
            Assert.DoesNotContain("MADE IN CHINA", html);
        }
        else
        {
            Assert.Empty(document.DocumentNode.Descendants("img"));
            Assert.Contains("&lt;script&gt;do not execute&lt;/script&gt; &amp;", html);
            Assert.Contains("唛头\nMADE IN CHINA", html);
        }
        Assert.Equal(type, invoice.ShippingMarksType);
        Assert.Equal(image.ImagePath, invoice.ShippingMarksImage);
        Assert.StartsWith("N/M <script>", invoice.ShippingMarks!);
    }

    [Fact]
    public async Task EmptyText_UsesTemplateFallbackAndNeverReadsInactiveImage()
    {
        var globals = await ReportTemplateGlobalsBuilder.BuildInvoiceGlobalsAsync(
            new Invoice { ShippingMarksImage = "Marks/missing.png" }, null, null, false, _images);
        Assert.Equal("N/M", ScribanReportTemplateRenderer.Render(
            "{{ if Invoice.ShippingMarks }}{{ Invoice.ShippingMarks }}{{ else }}N/M{{ end }}", globals));
        Assert.False(Directory.Exists(Path.Combine(_paths.DataRoot, "Marks")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Marks/missing.png")]
    [InlineData("Marks/../outside.png")]
    [InlineData("Files/not-a-mark.png")]
    public async Task InvalidActiveImage_FailsExplicitlyInsteadOfUsingStaleText(string path)
    {
        var invoice = new Invoice { ShippingMarksType = "Image", ShippingMarksImage = path, ShippingMarks = "STALE" };
        var error = await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() =>
            ReportTemplateGlobalsBuilder.BuildInvoiceGlobalsAsync(invoice, null, null, false, _images));
        Assert.Contains("唛头图片", error.Message);
    }

    [Fact]
    public async Task CorruptImage_IsRejectedAtUploadAndEveryRead()
    {
        byte[] truncated = RasterImageFixtures.Read("png")[..20];
        await Assert.ThrowsAsync<FormatException>(() => _images.SavePngDataUrlAsync("data:image/png;base64," + Convert.ToBase64String(truncated)));
        Assert.False(Directory.Exists(Path.Combine(_paths.DataRoot, "Marks")));
        var image = await SaveImageAsync();
        string path = Path.Combine(_paths.DataRoot, image.ImagePath.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(path, truncated);
        await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() => _images.ReadImageAsDataUrlAsync(image.ImagePath));
        await Assert.ThrowsAsync<UserVisibleInfrastructureException>(() => ReportTemplateGlobalsBuilder.BuildInvoiceGlobalsAsync(
            new Invoice { ShippingMarksType = "Image", ShippingMarksImage = image.ImagePath }, null, null, false, _images));
    }

    [Fact]
    public async Task Cancellation_StopsImageAndReportWork()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _images.SavePngDataUrlAsync(
            "data:image/png;base64," + Convert.ToBase64String(RasterImageFixtures.Read("png")), source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReportTemplateGlobalsBuilder.BuildInvoiceGlobalsAsync(
            new Invoice(), null, null, false, _images, cancellationToken: source.Token));
    }

    [Fact]
    public void FieldCatalog_ContainsOneShippingMarkAndKeepsPaymentDomainSeparate()
    {
        var catalog = new ReportTemplateFieldCatalogService();
        Assert.Single(catalog.GetFieldCatalog(ReportDocumentType.ExportDocument).Fields,
            field => field.Label.Contains("唛头", StringComparison.Ordinal) && field.Value == "{{ Invoice.ShippingMarks }}");
        Assert.DoesNotContain("shipping_marks_image_data", ReportTemplateV3ContractCatalog.ControlledImageFieldPaths);
        Assert.DoesNotContain(catalog.GetFieldCatalog(ReportDocumentType.PaymentVoucher).Fields, field => field.Value.Contains("ShippingMarks", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ReportTemplateContentPolicy.Validate(ReportDocumentType.PaymentVoucher, "<p>{{ Invoice.ShippingMarks }}</p>"));
        Assert.Throws<ArgumentException>(() => ReportTemplateContentPolicy.Validate(ReportDocumentType.ExportDocument, "<img src=\"{{ shipping_marks_image_data }}\">"));
    }

    private Task<ShippingMarkImageSaveResult> SaveImageAsync() =>
        _images.SavePngDataUrlAsync("data:image/png;base64," + Convert.ToBase64String(RasterImageFixtures.Read("png")));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class BatchExportPathHelperTests
{
    [Fact]
    public void SanitizePart_ShouldRejectPortableDeviceNames()
    {
        Assert.Equal("_CON", BatchExportPathHelper.SanitizePart("CON"));
        Assert.Equal("_LPT1.pdf", BatchExportPathHelper.SanitizePart("LPT1.pdf"));
    }

    [Fact]
    public void BuildDocumentFileName_ShouldAvoidCaseInsensitivePortableCollisions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Invoice.pdf"), string.Empty);

            string fileName = BatchExportPathHelper.BuildDocumentFileName(
                directory,
                "{InvoiceNo}",
                "invoice",
                string.Empty,
                string.Empty,
                new DateOnly(2026, 8, 19));

            Assert.Equal("invoice_1.pdf", fileName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

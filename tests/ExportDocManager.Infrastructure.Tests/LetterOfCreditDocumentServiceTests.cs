using ExportDocManager.Services.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class LetterOfCreditDocumentServiceTests
{
    [Fact]
    public async Task ScannedPdf_ShouldRasterizePagesAndUseOcr()
    {
        string root = Path.Combine(Path.GetTempPath(), $"edm-lc-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string pdfPath = Path.Combine(root, "scan.pdf");
        try
        {
            CreateGraphicsOnlyPdf(pdfPath);
            var ocr = new StubOcrService("IRREVOCABLE DOCUMENTARY CREDIT");
            var service = new LetterOfCreditDocumentService(ocr);

            LetterOfCreditDocumentImportResult result = await service.ImportAsync(pdfPath);

            Assert.Equal("PDF", result.SourceDescription);
            Assert.Contains("IRREVOCABLE DOCUMENTARY CREDIT", result.ExtractedText, StringComparison.Ordinal);
            Assert.Equal(1, ocr.CallCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateGraphicsOnlyPdf(string path)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(XBrushes.Black, 60, 80, 460, 50);
        graphics.DrawRectangle(XBrushes.Black, 60, 160, 360, 24);
        graphics.DrawRectangle(XBrushes.Black, 60, 210, 420, 24);
        document.Save(path);
    }

    private sealed class StubOcrService(string text) : IOcrService
    {
        public int CallCount { get; private set; }

        public Task<OcrResult> RecognizeAsync(Stream imageStream)
        {
            CallCount++;
            Assert.True(imageStream.Length > 0);
            return Task.FromResult(new OcrResult { FullText = text });
        }
    }
}

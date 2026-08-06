using ExportDocManager.Services.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class LetterOfCreditDocumentServiceTests
{
    [Fact]
    public async Task ImportAsync_ShouldRejectEmptyOversizedAndOverlongTextFiles()
    {
        string root = CreateTestRoot("limits");
        try
        {
            var service = new LetterOfCreditDocumentService(new StubOcrService("unused"));
            string emptyPath = Path.Combine(root, "empty.txt");
            await File.WriteAllBytesAsync(emptyPath, []);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(emptyPath));

            string oversizedPath = Path.Combine(root, "oversized.txt");
            await using (var stream = new FileStream(oversizedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(LetterOfCreditDocumentService.MaximumFileBytes + 1);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(oversizedPath));

            string overlongTextPath = Path.Combine(root, "overlong.txt");
            await File.WriteAllTextAsync(
                overlongTextPath,
                new string('A', LetterOfCreditDocumentService.MaximumExtractedTextCharacters + 1));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(overlongTextPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldRejectPdfOverPageLimitBeforeOcr()
    {
        string root = CreateTestRoot("pages");
        string pdfPath = Path.Combine(root, "too-many-pages.pdf");
        try
        {
            using (var document = new PdfDocument())
            {
                for (int index = 0; index < LetterOfCreditDocumentService.MaximumPdfPages + 1; index++)
                {
                    document.AddPage();
                }
                document.Save(pdfPath);
            }

            var ocr = new StubOcrService("unused");
            var service = new LetterOfCreditDocumentService(ocr);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(pdfPath));

            Assert.Contains("页数", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, ocr.CallCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldReportMalformedPdfAsInvalidData()
    {
        string root = CreateTestRoot("malformed");
        string pdfPath = Path.Combine(root, "malformed.pdf");
        try
        {
            await File.WriteAllTextAsync(pdfPath, "%PDF-1.7\nthis is not a valid PDF document");
            var ocr = new StubOcrService("unused");
            var service = new LetterOfCreditDocumentService(ocr);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(pdfPath));

            Assert.Contains("PDF", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, ocr.CallCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScannedPdf_ShouldRasterizePagesAndUseOcr()
    {
        string root = CreateTestRoot("scan");
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

    private static string CreateTestRoot(string suffix)
    {
        string root = Path.Combine(
            FindRepositoryRoot(),
            ".codex-runtime",
            "letter-of-credit-document-tests",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ExportDocManager.sln from test output.");
    }

    private sealed class StubOcrService(string text) : IOcrService
    {
        public int CallCount { get; private set; }

        public Task<OcrResult> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Assert.True(imageStream.Length > 0);
            return Task.FromResult(new OcrResult { FullText = text });
        }
    }
}

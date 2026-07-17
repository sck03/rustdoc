using System.IO;
using System.Threading;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ExportDocManager.Services.Tools
{
    public sealed class LetterOfCreditDocumentService : ILetterOfCreditDocumentService
    {
        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt",
            ".md",
            ".csv",
            ".json",
            ".xml"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff",
            ".webp"
        };

        private readonly IOcrService _ocrService;

        public LetterOfCreditDocumentService(IOcrService ocrService)
        {
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        }

        public async Task<LetterOfCreditDocumentImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("信用证文件路径不能为空。", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("找不到指定的信用证文件。", filePath);
            }

            string extension = Path.GetExtension(filePath);
            string extractedText;
            string sourceDescription;

            if (TextExtensions.Contains(extension))
            {
                extractedText = await File.ReadAllTextAsync(filePath, cancellationToken);
                sourceDescription = "文本文件";
            }
            else if (ImageExtensions.Contains(extension))
            {
                extractedText = await ExtractTextFromImageAsync(filePath, cancellationToken);
                sourceDescription = "图片 OCR";
            }
            else if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                extractedText = await ExtractTextFromPdfAsync(filePath, cancellationToken);
                sourceDescription = "PDF";
            }
            else
            {
                throw new NotSupportedException($"暂不支持导入 {extension} 类型的信用证文件。");
            }

            extractedText = NormalizeText(extractedText);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new InvalidOperationException("未能从信用证文件中提取到有效文本。");
            }

            return new LetterOfCreditDocumentImportResult
            {
                SourcePath = filePath,
                ExtractedText = extractedText,
                SourceDescription = sourceDescription
            };
        }

        private async Task<string> ExtractTextFromImageAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(filePath);
            var result = await _ocrService.RecognizeAsync(stream);
            return GetOcrText(result);
        }

        private async Task<string> ExtractTextFromPdfAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directText = ExtractTextFromPdfWithPdfPig(filePath);
            if (LooksLikeUsefulPdfText(directText))
            {
                return directText;
            }

            byte[] pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return await ExtractTextFromPdfImagesAsync(pdfBytes, cancellationToken);
        }

        private static string ExtractTextFromPdfWithPdfPig(string filePath)
        {
            var pages = new List<string>();

            using var document = PdfDocument.Open(filePath);
            foreach (var page in document.GetPages())
            {
                string pageText = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    pages.Add(pageText);
                }
            }

            return string.Join(Environment.NewLine + Environment.NewLine, pages);
        }

        private async Task<string> ExtractTextFromPdfImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            var texts = new List<string>();

            // PDFtoImage supports the desktop platforms targeted by the sidecar.
#pragma warning disable CA1416
            foreach (SKBitmap bitmap in Conversion.ToImages(pdfBytes))
#pragma warning restore CA1416
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (bitmap)
                using (var image = SKImage.FromBitmap(bitmap))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                await using (var stream = new MemoryStream())
                {
                    data.SaveTo(stream);
                    stream.Position = 0;
                    var result = await _ocrService.RecognizeAsync(stream);
                    string pageText = GetOcrText(result);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        texts.Add(pageText);
                    }
                }
            }

            return string.Join(Environment.NewLine + Environment.NewLine, texts);
        }

        private static bool LooksLikeUsefulPdfText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = NormalizeText(text);
            int meaningfulCharacters = normalized.Count(ch => !char.IsWhiteSpace(ch) && !char.IsControl(ch));
            return meaningfulCharacters >= 20;
        }

        private static string GetOcrText(OcrResult result)
        {
            if (!string.IsNullOrWhiteSpace(result?.FullText))
            {
                return result.FullText;
            }

            return string.Join(
                Environment.NewLine,
                result?.Lines?
                    .Where(line => !string.IsNullOrWhiteSpace(line?.Text))
                    .Select(line => line.Text)
                ?? Enumerable.Empty<string>());
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();
        }
    }
}

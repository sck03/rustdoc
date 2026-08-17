using System.Collections.Frozen;
using System.Drawing;
using System.IO;
using System.Threading;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Tools
{
    public sealed class LetterOfCreditDocumentService : ILetterOfCreditDocumentService
    {
        public const long MaximumFileBytes = 25L * 1024L * 1024L;
        public const int MaximumPdfPages = 50;
        public const int MaximumExtractedTextCharacters = 500_000;
        public const int PdfRenderDpi = 200;
        public const long MaximumPdfRenderPixelsPerPage = 12_000_000;
        public const long MaximumPdfRenderPixelsTotal = 200_000_000;
        public const int MaximumPdfRenderDimension = 10_000;
        private static readonly RenderOptions PdfRenderOptions = new(
            Dpi: PdfRenderDpi,
            UseTiling: true,
            Grayscale: true);
        private static readonly FrozenSet<string> TextExtensions = new[]
        {
            ".txt",
            ".md",
            ".csv",
            ".json",
            ".xml"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> ImageExtensions = new[]
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".gif",
            ".tif",
            ".tiff",
            ".webp"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

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

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumFileBytes)
            {
                throw new InvalidDataException("信用证文件为空或超过 25 MB 限制。");
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
                throw new ServiceValidationException("未能从信用证文件中提取到有效文本。");
            }

            if (extractedText.Length > MaximumExtractedTextCharacters)
            {
                throw new InvalidDataException("信用证提取文本超过允许长度，请拆分文件后重试。");
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
            var result = await _ocrService.RecognizeAsync(stream, cancellationToken);
            return GetOcrText(result);
        }

        private async Task<string> ExtractTextFromPdfAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string directText = ExtractTextFromPdfWithPdfPig(filePath);
                if (LooksLikeUsefulPdfText(directText))
                {
                    return directText;
                }

                byte[] pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                return await ExtractTextFromPdfImagesAsync(pdfBytes, cancellationToken);
            }
            catch (PdfDocumentFormatException ex)
            {
                throw new InvalidDataException("信用证 PDF 已损坏、格式无效或不受支持。", ex);
            }
            catch (PdfException ex)
            {
                throw new InvalidDataException("信用证 PDF 已损坏、受密码保护或格式不受支持。", ex);
            }
        }

        private static string ExtractTextFromPdfWithPdfPig(string filePath)
        {
            var pages = new List<string>();

            using var document = PdfDocument.Open(filePath);
            if (document.NumberOfPages <= 0 || document.NumberOfPages > MaximumPdfPages)
            {
                throw new InvalidDataException($"信用证 PDF 页数必须在 1 至 {MaximumPdfPages} 页以内。");
            }

            int characters = 0;
            foreach (var page in document.GetPages())
            {
                string pageText = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    characters += pageText.Length;
                    if (characters > MaximumExtractedTextCharacters)
                    {
                        throw new InvalidDataException("信用证提取文本超过允许长度，请拆分文件后重试。");
                    }
                    pages.Add(pageText);
                }
            }

            return string.Join(Environment.NewLine + Environment.NewLine, pages);
        }

        private async Task<string> ExtractTextFromPdfImagesAsync(byte[] pdfBytes, CancellationToken cancellationToken)
        {
            var texts = new List<string>();
            int characters = 0;

            // PDFtoImage supports the desktop platforms targeted by the sidecar.
#pragma warning disable CA1416
            ValidatePdfRenderBudget(Conversion.GetPageSizes(pdfBytes));
            await foreach (SKBitmap bitmap in Conversion.ToImagesAsync(
                pdfBytes,
                options: PdfRenderOptions,
                cancellationToken: cancellationToken))
#pragma warning restore CA1416
            {
                cancellationToken.ThrowIfCancellationRequested();

                long bitmapPixels = checked((long)bitmap.Width * bitmap.Height);
                if (bitmap.Width > MaximumPdfRenderDimension ||
                    bitmap.Height > MaximumPdfRenderDimension ||
                    bitmapPixels > MaximumPdfRenderPixelsPerPage)
                {
                    bitmap.Dispose();
                    throw new InvalidDataException("信用证 PDF 页面渲染尺寸超过安全限制，请缩小页面后重试。");
                }

                using (bitmap)
                using (var image = SKImage.FromBitmap(bitmap))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                await using (var stream = new MemoryStream())
                {
                    data.SaveTo(stream);
                    stream.Position = 0;
                    var result = await _ocrService.RecognizeAsync(stream, cancellationToken);
                    string pageText = GetOcrText(result);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        characters += pageText.Length;
                        if (characters > MaximumExtractedTextCharacters)
                        {
                            throw new InvalidDataException("信用证提取文本超过允许长度，请拆分文件后重试。");
                        }
                        texts.Add(pageText);
                    }
                }
            }

            return string.Join(Environment.NewLine + Environment.NewLine, texts);
        }

        internal static void ValidatePdfRenderBudget(IList<SizeF> pageSizes)
        {
            ArgumentNullException.ThrowIfNull(pageSizes);
            if (pageSizes.Count <= 0 || pageSizes.Count > MaximumPdfPages)
            {
                throw new InvalidDataException($"信用证 PDF 页数必须在 1 至 {MaximumPdfPages} 页以内。");
            }

            long totalPixels = 0;
            foreach (SizeF pageSize in pageSizes)
            {
                if (!float.IsFinite(pageSize.Width) || !float.IsFinite(pageSize.Height) ||
                    pageSize.Width <= 0 || pageSize.Height <= 0)
                {
                    throw new InvalidDataException("信用证 PDF 包含无效页面尺寸。");
                }

                long width = checked((long)Math.Ceiling(pageSize.Width * PdfRenderDpi / 72d));
                long height = checked((long)Math.Ceiling(pageSize.Height * PdfRenderDpi / 72d));
                long pagePixels = checked(width * height);
                if (width > MaximumPdfRenderDimension ||
                    height > MaximumPdfRenderDimension ||
                    pagePixels > MaximumPdfRenderPixelsPerPage)
                {
                    throw new InvalidDataException("信用证 PDF 页面尺寸超过 OCR 安全限制，请缩小页面后重试。");
                }

                totalPixels = checked(totalPixels + pagePixels);
                if (totalPixels > MaximumPdfRenderPixelsTotal)
                {
                    throw new InvalidDataException("信用证 PDF 总渲染像素超过安全限制，请拆分文件后重试。");
                }
            }
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

using System.Globalization;
using System.Net;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;
using Scriban.Runtime;

namespace ExportDocManager.Services.Reporting
{
    internal static class ReportTemplateGlobalsBuilder
    {
        public static async Task<ScriptObject> BuildInvoiceGlobalsAsync(
            Invoice invoice,
            Customer? customer,
            Exporter? exporter,
            bool withSeal,
            IShippingMarkImageService? shippingMarkImages = null,
            IAppPathProvider? pathProvider = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            invoice.Items ??= new List<Item>();

            var scriptObject = new ScriptObject();
            var reportInvoice = new ScriptObject();
            reportInvoice.Import(invoice, renamer: member => member.Name);
            reportInvoice.SetValue(nameof(Invoice.ShippingMarks),
                await RenderShippingMarksAsync(invoice, shippingMarkImages, cancellationToken).ConfigureAwait(false), true);
            scriptObject.Add("Invoice", reportInvoice);
            scriptObject.Add("Customer", customer);
            scriptObject.Add("Exporter", exporter);

            scriptObject.Import(new
            {
                total_amount_words = ConvertNumberToWords(invoice.TotalAmount),
                total_by_ctn_unit = invoice.Items
                    .GroupBy(i => i.CtnUnitEN ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Cartons)),
                total_by_qty_unit = invoice.Items
                    .GroupBy(i => i.UnitEN ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity)),
            });

            scriptObject.Add("items", invoice.Items);
            AddSharedHelpers(scriptObject);
            scriptObject.Add("ShowSeal", withSeal);
            scriptObject.Add("withSeal", withSeal);

            if (withSeal)
            {
                scriptObject.Add("doc_seal_path", ReportImageDataUriHelper.GetSealDataUri(exporter?.DocSealPath, pathProvider, logger));
                scriptObject.Add("customs_seal_path", ReportImageDataUriHelper.GetSealDataUri(exporter?.CustomsSealPath, pathProvider, logger));
            }

            return scriptObject;
        }

        private static async Task<string?> RenderShippingMarksAsync(
            Invoice invoice, IShippingMarkImageService? images, CancellationToken cancellationToken)
        {
            string type = ShippingMarksTypeCatalog.Normalize(invoice.ShippingMarksType);
            if (type == ShippingMarksTypeCatalog.Text)
                return string.IsNullOrWhiteSpace(invoice.ShippingMarks) ? null : WebUtility.HtmlEncode(invoice.ShippingMarks);
            if (type != ShippingMarksTypeCatalog.Image)
                throw new ServiceValidationException("唛头类型只能是文本或图片。");
            if (images == null)
                throw new UserVisibleInfrastructureException("唛头图片读取服务不可用。");

            try
            {
                var image = await images.ReadImageAsDataUrlAsync(invoice.ShippingMarksImage ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                return $"<img class=\"edm-shipping-marks-image\" src=\"{image.DataUrl}\" alt=\"唛头\" style=\"display:inline-block;max-width:100%;max-height:var(--edm-field-image-height,60mm);width:auto;height:auto;object-fit:contain;vertical-align:top\">";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                throw new UserVisibleInfrastructureException("唛头图片无法读取，请重新编辑并保存该发票的唛头图片。", ex);
            }
        }

        public static ScriptObject BuildPaymentVoucherGlobals(
            Payment payment,
            Payee? payee)
        {
            payment ??= new Payment();
            var scriptObject = new ScriptObject();
            scriptObject.Add("Payment", payment);
            scriptObject.Add("Payee", payee ?? new Payee());
            scriptObject.Import(new
            {
                cny_amount_upper = ConvertNumberToChineseUpper(payment.CNYAmount)
            });

            AddSharedHelpers(scriptObject);
            return scriptObject;
        }

        private static void AddSharedHelpers(ScriptObject scriptObject)
        {
            scriptObject.Import("convert_to_words", new Func<decimal, string>(ConvertNumberToWords));
            scriptObject.Import("convert_to_chinese_upper", new Func<decimal, string>(ConvertNumberToChineseUpper));
            scriptObject.Import("format_date", new Func<object?, string, string>(FormatDate));
            scriptObject.Import("format_number", new Func<decimal, string, string>((number, format) =>
                number.ToString(format, CultureInfo.InvariantCulture)));
            scriptObject.Import("format_currency", new Func<decimal, string, string>((number, currency) =>
                $"{currency} {number.ToString("N2", CultureInfo.InvariantCulture)}"));
            scriptObject.Import("format_unit_price", new Func<decimal, string>(ItemPricePrecisionPolicy.Format));
            scriptObject.Import("format_weight", new Func<decimal, string>(ItemMeasurementPrecisionPolicy.FormatWeight));
            scriptObject.Import("format_volume", new Func<decimal, string>(ItemMeasurementPrecisionPolicy.FormatVolume));
        }

        private static string ConvertNumberToWords(decimal number) => NumberHelper.ToEnglishWords(number) + " ONLY";

        private static string ConvertNumberToChineseUpper(decimal number) => NumberHelper.ToChineseMoney(number);

        private static string FormatDate(object? value, string format)
        {
            string normalizedFormat = string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd" : format;
            return value switch
            {
                DateOnly date => date.ToString(normalizedFormat, CultureInfo.InvariantCulture),
                DateTimeOffset timestamp => timestamp.ToString(normalizedFormat, CultureInfo.InvariantCulture),
                DateTime dateTime => dateTime.ToString(normalizedFormat, CultureInfo.InvariantCulture),
                _ => string.Empty
            };
        }
    }

    internal static class ReportImageDataUriHelper
    {
        private const long MaximumImageBytes = 5L * 1024L * 1024L;

        public static string GetSealDataUri(
            string? path,
            IAppPathProvider? pathProvider,
            ILogger? logger = null)
        {
            if (pathProvider == null || string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string sealRoot = Path.Combine(pathProvider.FileRoot, "Seals");
            try
            {
                string resolved = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : ManagedDataPathResolver.ResolveStoredPath(
                        pathProvider,
                        path,
                        sealRoot,
                        "Files");
                return GetDataUri(resolved, [sealRoot, pathProvider.ResourceRoot], logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Blocked invalid seal image path: {Path}", path);
                return string.Empty;
            }
        }

        private static string GetDataUri(
            string path,
            IReadOnlyList<string> allowedRoots,
            ILogger? logger)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string? allowedRoot = allowedRoots?.FirstOrDefault(root =>
                    PathBoundaryHelper.IsWithinRoot(fullPath, root));
                if (string.IsNullOrWhiteSpace(allowedRoot) ||
                    !File.Exists(fullPath) ||
                    HasReparsePointBelowRoot(fullPath, allowedRoot))
                {
                    logger?.LogWarning("Blocked report image outside managed roots: {Path}", fullPath);
                    return string.Empty;
                }

                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length <= 0 || fileInfo.Length > MaximumImageBytes)
                {
                    return string.Empty;
                }

                byte[] imageBytes = File.ReadAllBytes(fullPath);
                string mimeType = DetectImageMimeType(imageBytes);
                if (string.IsNullOrWhiteSpace(mimeType)) return string.Empty;

                return $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to load report image: {Path}", path);
                return string.Empty;
            }
        }

        private static bool HasReparsePointBelowRoot(string path, string root)
        {
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? current = Path.GetFullPath(path);
            while (!string.Equals(current, fullRoot, PathBoundaryHelper.PathComparison))
            {
                if (string.IsNullOrWhiteSpace(current))
                {
                    return true;
                }
                if (!PathBoundaryHelper.IsWithinRoot(current, fullRoot))
                {
                    return true;
                }
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                current = Path.GetDirectoryName(current);
            }
            return false;
        }

        private static string DetectImageMimeType(byte[] bytes)
        {
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return "image/png";
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (bytes.Length >= 6 &&
                (bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
            {
                return "image/gif";
            }

            if (bytes.Length >= 12 &&
                bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            {
                return "image/webp";
            }

            return string.Empty;
        }
    }
}

using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using Scriban.Runtime;
using Serilog;

namespace ExportDocManager.Services.Reporting
{
    internal static class ReportTemplateGlobalsBuilder
    {
        public static ScriptObject BuildInvoiceGlobals(
            Invoice invoice,
            Customer customer,
            Exporter exporter,
            bool withSeal,
            IAppPathProvider pathProvider = null)
        {
            invoice.Items ??= new List<Item>();

            var scriptObject = new ScriptObject();
            scriptObject.Add("Invoice", invoice);
            scriptObject.Add("Customer", customer);
            scriptObject.Add("Exporter", exporter);

            scriptObject.Import(new
            {
                total_amount_words = ConvertNumberToWords(invoice.TotalAmount),
                total_by_ctn_unit = invoice.Items.GroupBy(i => i.CtnUnitEN).ToDictionary(g => g.Key, g => g.Sum(i => i.Cartons)),
                total_by_qty_unit = invoice.Items.GroupBy(i => i.UnitEN).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity)),
            });

            scriptObject.Add("items", invoice.Items);
            AddSharedHelpers(scriptObject);
            scriptObject.Add("ShowSeal", withSeal);
            scriptObject.Add("withSeal", withSeal);

            if (withSeal)
            {
                scriptObject.Add("doc_seal_path", ReportImageDataUriHelper.GetSealDataUri(exporter?.DocSealPath, pathProvider));
                scriptObject.Add("customs_seal_path", ReportImageDataUriHelper.GetSealDataUri(exporter?.CustomsSealPath, pathProvider));
            }

            if (invoice.ShippingMarksType == "Image" && !string.IsNullOrWhiteSpace(invoice.ShippingMarksImage))
            {
                scriptObject.Add("shipping_marks_image_data", ReportImageDataUriHelper.GetShippingMarkDataUri(invoice.ShippingMarksImage, pathProvider));
            }

            return scriptObject;
        }

        public static ScriptObject BuildPaymentVoucherGlobals(
            Payment payment,
            Payee payee)
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
            scriptObject.Import("format_date", new Func<DateTime, string, string>((date, format) => date.ToString(format)));
            scriptObject.Import("format_number", new Func<decimal, string, string>((number, format) => number.ToString(format)));
            scriptObject.Import("format_currency", new Func<decimal, string, string>((number, currency) => $"{currency} {number:N2}"));
            scriptObject.Import("format_unit_price", new Func<decimal, string>(ItemPricePrecisionPolicy.Format));
            scriptObject.Import("format_weight", new Func<decimal, string>(ItemMeasurementPrecisionPolicy.FormatWeight));
            scriptObject.Import("format_volume", new Func<decimal, string>(ItemMeasurementPrecisionPolicy.FormatVolume));
        }

        private static string ConvertNumberToWords(decimal number) => NumberHelper.ToEnglishWords(number) + " ONLY";

        private static string ConvertNumberToChineseUpper(decimal number) => NumberHelper.ToChineseMoney(number);
    }

    internal static class ReportImageDataUriHelper
    {
        private const long MaximumImageBytes = 5L * 1024L * 1024L;

        public static string GetShippingMarkDataUri(string path, IAppPathProvider pathProvider)
        {
            if (pathProvider == null)
            {
                return string.Empty;
            }
            string marksRoot = Path.Combine(pathProvider.DataRoot, "Marks");
            try
            {
                string resolved = ManagedDataPathResolver.ResolveStoredPath(
                    pathProvider,
                    path,
                    marksRoot,
                    "Marks");
                return GetDataUri(resolved, [marksRoot]);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Blocked invalid shipping mark image path: {Path}", path);
                return string.Empty;
            }
        }

        public static string GetSealDataUri(string path, IAppPathProvider pathProvider)
        {
            if (pathProvider == null)
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
                return GetDataUri(resolved, [sealRoot, pathProvider.ResourceRoot]);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Blocked invalid seal image path: {Path}", path);
                return string.Empty;
            }
        }

        private static string GetDataUri(string path, IReadOnlyList<string> allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string allowedRoot = allowedRoots?.FirstOrDefault(root =>
                    PathBoundaryHelper.IsWithinRoot(fullPath, root));
                if (string.IsNullOrWhiteSpace(allowedRoot) ||
                    !File.Exists(fullPath) ||
                    HasReparsePointBelowRoot(fullPath, allowedRoot))
                {
                    Log.Warning("Blocked report image outside managed roots: {Path}", fullPath);
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
                Log.Error(ex, "Failed to load report image: {Path}", path);
                return string.Empty;
            }
        }

        private static bool HasReparsePointBelowRoot(string path, string root)
        {
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = Path.GetFullPath(path);
            while (!string.Equals(current, fullRoot, PathBoundaryHelper.PathComparison))
            {
                if (!PathBoundaryHelper.IsWithinRoot(current, fullRoot))
                {
                    return true;
                }
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(current))
                {
                    return true;
                }
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

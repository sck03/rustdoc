using ExportDocManager.Models.Entities;
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
            bool withSeal)
        {
            invoice.Items ??= new List<Item>();

            var scriptObject = new ScriptObject();
            scriptObject.Add("Invoice", invoice);
            scriptObject.Add("Customer", customer);
            scriptObject.Add("Exporter", exporter);
            // Invoice/customs reports do not read payment/reimbursement records by invoice number.
            scriptObject.Add("Payment", new Payment());

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
                scriptObject.Add("doc_seal_path", ReportImageDataUriHelper.GetDataUri(exporter?.DocSealPath));
                scriptObject.Add("customs_seal_path", ReportImageDataUriHelper.GetDataUri(exporter?.CustomsSealPath));
            }

            if (invoice.ShippingMarksType == "Image" && !string.IsNullOrWhiteSpace(invoice.ShippingMarksImage))
            {
                scriptObject.Add("shipping_marks_image_data", ReportImageDataUriHelper.GetDataUri(invoice.ShippingMarksImage));
            }

            return scriptObject;
        }

        public static ScriptObject BuildPaymentVoucherGlobals(
            Exporter exporter,
            Payment payment,
            Payee payee,
            bool withSeal)
        {
            payment ??= new Payment();
            var scriptObject = new ScriptObject();
            scriptObject.Add("Exporter", exporter ?? new Exporter());
            scriptObject.Add("Payment", payment);
            scriptObject.Add("Payee", payee ?? new Payee());
            // Payment vouchers are independent from invoice/customs documents; these aliases are detached legacy template compatibility objects.
            scriptObject.Add("Invoice", new Invoice { InvoiceNo = payment.InvoiceNo ?? string.Empty });
            scriptObject.Add("Customer", new Customer());

            scriptObject.Import(new
            {
                cny_amount_upper = ConvertNumberToChineseUpper(payment.CNYAmount),
                doc_seal_path = withSeal ? ReportImageDataUriHelper.GetDataUri(exporter?.DocSealPath) : string.Empty,
                customs_seal_path = withSeal ? ReportImageDataUriHelper.GetDataUri(exporter?.CustomsSealPath) : string.Empty
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
        }

        private static string ConvertNumberToWords(decimal number) => NumberHelper.ToEnglishWords(number) + " ONLY";

        private static string ConvertNumberToChineseUpper(decimal number) => NumberHelper.ToChineseMoney(number);
    }

    internal static class ReportImageDataUriHelper
    {
        public static string GetDataUri(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                byte[] imageBytes = File.ReadAllBytes(path);
                string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                string mimeType = extension switch
                {
                    "jpg" or "jpeg" => "image/jpeg",
                    "png" => "image/png",
                    "gif" => "image/gif",
                    "webp" => "image/webp",
                    "svg" => "image/svg+xml",
                    _ => $"image/{extension}"
                };

                return $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load report image: {Path}", path);
                return string.Empty;
            }
        }
    }
}

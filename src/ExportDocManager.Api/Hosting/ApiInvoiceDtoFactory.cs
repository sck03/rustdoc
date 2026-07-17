namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiInvoiceDtoFactory
    {
        private const string DefaultInvoiceType = "实际数据";

        private static byte[] DecodeRowVersion(string rowVersion)
        {
            return string.IsNullOrWhiteSpace(rowVersion)
                ? null
                : Convert.FromBase64String(rowVersion);
        }

        private static string NormalizeInvoiceType(string invoiceType)
        {
            string normalized = invoiceType?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(normalized)
                ? DefaultInvoiceType
                : normalized;
        }
    }
}

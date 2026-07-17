namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowPayloadFileNameHelper
    {
        public static string BuildBaseFileName(string preferredName, string fallbackName, string extension)
        {
            var safeName = string.IsNullOrWhiteSpace(preferredName)
                ? fallbackName
                : SanitizeFileName(preferredName);
            return safeName + extension;
        }

        public static string ResolveDocType(string fileName)
        {
            return Path.GetExtension(fileName).Trim('.').ToUpperInvariant();
        }

        public static string ResolveCooAttachmentFileType(string fileName)
        {
            string normalized = (fileName ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "7";
            }

            if (normalized.Contains("第三方", StringComparison.Ordinal) ||
                normalized.Contains("NONPARTY", StringComparison.Ordinal) ||
                normalized.Contains("THIRDPARTY", StringComparison.Ordinal))
            {
                return "2";
            }

            if (normalized.Contains("发票", StringComparison.Ordinal) ||
                normalized.Contains("INVOICE", StringComparison.Ordinal))
            {
                return "1";
            }

            if (normalized.Contains("提单", StringComparison.Ordinal) ||
                normalized.Contains("运单", StringComparison.Ordinal) ||
                normalized.Contains("运输", StringComparison.Ordinal) ||
                normalized.Contains("BILL", StringComparison.Ordinal) ||
                normalized.Contains("B/L", StringComparison.Ordinal) ||
                normalized.Contains("TRANSPORT", StringComparison.Ordinal))
            {
                return "3";
            }

            if (normalized.Contains("报关", StringComparison.Ordinal) ||
                normalized.Contains("报关单", StringComparison.Ordinal) ||
                normalized.Contains("DECLARATION", StringComparison.Ordinal))
            {
                return "4";
            }

            if (normalized.Contains("成本", StringComparison.Ordinal) ||
                normalized.Contains("COST", StringComparison.Ordinal))
            {
                return "5";
            }

            if (normalized.Contains("采购", StringComparison.Ordinal) ||
                normalized.Contains("PURCHASE", StringComparison.Ordinal))
            {
                return "6";
            }

            if (normalized.Contains("原证", StringComparison.Ordinal) ||
                normalized.Contains("原产证", StringComparison.Ordinal) ||
                normalized.Contains("CERTIFICATE", StringComparison.Ordinal))
            {
                return "15";
            }

            return "7";
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "package" : sanitized;
        }
    }
}

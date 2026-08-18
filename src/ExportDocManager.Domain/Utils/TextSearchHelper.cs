namespace ExportDocManager.Utils
{
    public static class TextSearchHelper
    {
        public static string NormalizeFilter(string? value)
        {
            return NormalizeValue(value);
        }

        public static string NormalizeValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static string NormalizeUpperValue(string? value)
        {
            return NormalizeValue(value).ToUpperInvariant();
        }

        public static string[] Tokenize(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Array.Empty<string>();
            }

            return keyword
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

    }
}

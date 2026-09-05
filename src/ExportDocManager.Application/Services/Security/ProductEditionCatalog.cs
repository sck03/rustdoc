namespace ExportDocManager.Services.Security
{
    public static class ProductEditionCatalog
    {
        public const string Document = "Document";
        public const string Sales = "Sales";
        public const string Full = "Full";

        public static readonly IReadOnlyList<string> Editions = [Document, Sales, Full];

        public static bool IsKnown(string edition) =>
            Editions.Any(item => string.Equals(item, edition?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string Normalize(string edition)
        {
            var normalized = (edition ?? string.Empty).Trim();
            // An omitted value is the documented desktop/full default.  A
            // non-empty unknown value is a deployment/configuration error and
            // must never silently unlock the Full edition.
            if (normalized.Length == 0)
            {
                return Full;
            }

            return Editions.FirstOrDefault(item =>
                       string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException(
                       $"未知产品版本“{normalized}”；允许值为 {string.Join("、", Editions)}。");
        }

        public static bool IncludesDocumentWorkspace(string edition) =>
            Normalize(edition) is Document or Full;

        public static bool IncludesSalesWorkspace(string edition) =>
            Normalize(edition) is Sales or Full;
    }
}

namespace ExportDocManager.Services.Security
{
    public static class ProductEditionCatalog
    {
        public const string Document = "Document";
        public const string Sales = "Sales";
        public const string Full = "Full";

        public static readonly IReadOnlyList<string> Editions = [Document, Sales, Full];

        public static string Normalize(string edition)
        {
            var normalized = (edition ?? string.Empty).Trim();
            return Editions.FirstOrDefault(item =>
                       string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? Full;
        }

        public static bool IncludesDocumentWorkspace(string edition) =>
            Normalize(edition) is Document or Full;

        public static bool IncludesSalesWorkspace(string edition) =>
            Normalize(edition) is Sales or Full;
    }
}

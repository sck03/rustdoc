namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowUnitNormalizer
    {
        private static readonly Dictionary<string, string> UnitEnglishLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PCS"] = "PCS",
            ["PIECE"] = "PCS",
            ["PIECES"] = "PCS",
            ["件"] = "PCS",
            ["个"] = "PCS",
            ["EA"] = "PCS",
            ["SET"] = "SET",
            ["SETS"] = "SET",
            ["套"] = "SET",
            ["CTN"] = "CTN",
            ["CTNS"] = "CTN",
            ["CARTON"] = "CTN",
            ["CARTONS"] = "CTN",
            ["箱"] = "CTN",
            ["BOX"] = "BOX",
            ["BOXES"] = "BOX",
            ["盒"] = "BOX",
            ["KGS"] = "KGS",
            ["KG"] = "KGS",
            ["KILOGRAM"] = "KGS",
            ["KILOGRAMS"] = "KGS",
            ["千克"] = "KGS",
            ["公斤"] = "KGS"
        };

        private static readonly Dictionary<string, string> UnitChineseLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PCS"] = "件",
            ["PIECE"] = "件",
            ["PIECES"] = "件",
            ["件"] = "件",
            ["个"] = "件",
            ["EA"] = "件",
            ["SET"] = "套",
            ["SETS"] = "套",
            ["套"] = "套",
            ["CTN"] = "箱",
            ["CTNS"] = "箱",
            ["CARTON"] = "箱",
            ["CARTONS"] = "箱",
            ["箱"] = "箱",
            ["BOX"] = "盒",
            ["BOXES"] = "盒",
            ["盒"] = "盒",
            ["KGS"] = "千克",
            ["KG"] = "千克",
            ["KILOGRAM"] = "千克",
            ["KILOGRAMS"] = "千克",
            ["千克"] = "千克",
            ["公斤"] = "千克"
        };

        public static string NormalizeEnglish(string value)
        {
            string key = NormalizeLookupKey(value);
            return UnitEnglishLookup.TryGetValue(key, out var mapped)
                ? mapped
                : NormalizeUpperText(value);
        }

        public static string NormalizeChinese(string value)
        {
            string key = NormalizeLookupKey(value);
            return UnitChineseLookup.TryGetValue(key, out var mapped)
                ? mapped
                : NormalizeText(value);
        }

        private static string NormalizeLookupKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
        }

        private static string NormalizeUpperText(string value) => NormalizeText(value).ToUpperInvariant();

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

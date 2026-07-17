namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooTradeModeCatalog
    {
        private static readonly HashSet<string> CooTradeModeCodes =
            new(SingleWindowFieldValidationHelper.CooTradeModeCodeValues, StringComparer.Ordinal);

        private static readonly Dictionary<string, string> CooTradeModeLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            ["其他贸易方式"] = "0",
            ["其他贸易"] = "0",
            ["其他"] = "0",
            ["普通贸易"] = "1",
            ["一般贸易"] = "1",
            ["来料加工"] = "2",
            ["进料加工"] = "3",
            ["外商投资"] = "4",
            ["外商投资企业"] = "4",
            ["易货贸易"] = "5",
            ["补偿贸易"] = "6",
            ["边境贸易"] = "7",
            ["展卖贸易"] = "8",
            ["零售贸易"] = "9",
            ["无偿援助"] = "10",
            ["边民互市"] = "33",
            ["小额贸易"] = "34",
            ["边境小额贸易"] = "34"
        };

        public static IReadOnlyList<string> CodeValues => SingleWindowFieldValidationHelper.CooTradeModeCodeValues;

        public static string NormalizeCode(string value)
        {
            string normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (CooTradeModeCodes.Contains(normalized))
            {
                return normalized;
            }

            return CooTradeModeLookup.TryGetValue(NormalizeLookupKey(value), out string mapped)
                ? mapped
                : string.Empty;
        }

        public static string PreferCode(string preferredValue, string fallbackValue)
        {
            string normalizedPreferred = NormalizeCode(preferredValue);
            return string.IsNullOrWhiteSpace(normalizedPreferred)
                ? NormalizeCode(fallbackValue)
                : normalizedPreferred;
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

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Data
{
    internal static class ExcelImportFieldValueValidator
    {
        private static readonly Regex CurrencyAmountRegex = new(
            @"^(?:USD|CNY|RMB|EUR|GBP|AUD|JPY|HKD)?\s*[$¥€£]?\s*[\d,]+(?:\.\d+)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Lazy<IReadOnlyDictionary<string, string>> CountryNames = new(BuildCountryNames);

        public static bool IsDestinationCountry(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length is < 2 or > 80 || normalized.Count(char.IsLetter) < 2)
            {
                return false;
            }

            return !CurrencyAmountRegex.IsMatch(normalized)
                && !normalized.EndsWith('%');
        }

        public static bool TryNormalizeDestinationCountry(string value, out string country)
        {
            country = string.Empty;
            string key = NormalizeCountryKey(value);
            return !string.IsNullOrWhiteSpace(key)
                && CountryNames.Value.TryGetValue(key, out country);
        }

        public static bool TryInferDestinationCountry(string destination, string address, out string country)
        {
            if (TryNormalizeDestinationCountry(destination, out country))
            {
                return true;
            }

            foreach (string part in (address ?? string.Empty)
                .Replace('，', ',')
                .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Reverse())
            {
                if (TryNormalizeDestinationCountry(part, out country))
                {
                    return true;
                }
            }

            country = string.Empty;
            return false;
        }

        private static IReadOnlyDictionary<string, string> BuildCountryNames()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    string englishName = region.EnglishName.ToUpperInvariant();
                    AddCountryName(names, region.EnglishName, englishName);
                    AddCountryName(names, region.NativeName, englishName);
                    AddCountryName(names, region.TwoLetterISORegionName, englishName);
                    AddCountryName(names, region.ThreeLetterISORegionName, englishName);
                }
                catch (ArgumentException)
                {
                }
            }

            names["UK"] = "UNITED KINGDOM";
            names["USA"] = "UNITED STATES";
            names["UAE"] = "UNITED ARAB EMIRATES";
            return names;
        }

        private static void AddCountryName(IDictionary<string, string> names, string alias, string englishName)
        {
            string key = NormalizeCountryKey(alias);
            if (!string.IsNullOrWhiteSpace(key))
            {
                names[key] = englishName;
            }
        }

        private static string NormalizeCountryKey(string value)
        {
            return Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]", string.Empty);
        }
    }
}

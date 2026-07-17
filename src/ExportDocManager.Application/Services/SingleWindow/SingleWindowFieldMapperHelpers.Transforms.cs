using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public static partial class SingleWindowFieldMapperHelpers
    {
        public static string NormalizeText(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        public static string NormalizeUpperText(string value) => NormalizeText(value).ToUpperInvariant();

        public static string NormalizeCountryCode(string value)
        {
            if (TryResolveCountry(value, out var entry))
            {
                return entry.Code;
            }

            var normalized = NormalizeText(value);
            return normalized.All(char.IsDigit) && normalized.Length <= 4
                ? normalized
                : string.Empty;
        }

        public static string NormalizeAcdOriginCountryCode(string value)
        {
            var normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "142";
            }

            if (TryResolveAcdCountry(value, out var acdEntry))
            {
                return acdEntry.Code;
            }

            if (normalized.Length == 3 && normalized.All(char.IsDigit))
            {
                return normalized;
            }

            if (TryResolveCountry(value, out var entry))
            {
                if (string.Equals(entry.ChineseName, "中国", StringComparison.Ordinal) ||
                    string.Equals(entry.EnglishName, "CHINA", StringComparison.OrdinalIgnoreCase))
                {
                    return "142";
                }
            }

            return "142";
        }

        public static string ResolveAcdOriginCountryCode(string value)
        {
            return NormalizeAcdOriginCountryCode(value);
        }

        public static string NormalizeCountryNameEnglish(string value)
        {
            return TryResolveCountry(value, out var entry)
                ? entry.EnglishName
                : NormalizeUpperText(value);
        }

        public static string NormalizeCountryNameChinese(string value)
        {
            return TryResolveCountry(value, out var entry)
                ? entry.ChineseName
                : NormalizeText(value);
        }

        public static string NormalizeCurrencyText(string value)
        {
            if (TryResolveCurrencyByText(value, out var entry))
            {
                return entry.AlphaCode;
            }

            return NormalizeUpperText(value);
        }

        public static string NormalizePriceTerms(string value)
        {
            string normalized = NormalizeUpperText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string[] knownTerms =
            [
                "FOB",
                "CIF",
                "CFR",
                "EXW",
                "FCA",
                "CPT",
                "CIP",
                "DAP",
                "DPU",
                "DDP"
            ];

            foreach (string knownTerm in knownTerms)
            {
                if (string.Equals(normalized, knownTerm, StringComparison.Ordinal))
                {
                    return knownTerm;
                }

                if (normalized.StartsWith(knownTerm + " ", StringComparison.Ordinal) ||
                    normalized.StartsWith(knownTerm + "/", StringComparison.Ordinal) ||
                    normalized.StartsWith(knownTerm + "-", StringComparison.Ordinal) ||
                    normalized.StartsWith(knownTerm + "(", StringComparison.Ordinal))
                {
                    return knownTerm;
                }
            }

            return normalized;
        }

        public static string NormalizeCurrencyCode(string value)
        {
            string normalized = NormalizeText(value);
            if (TryResolveCurrencyByAcdCode(normalized, out var acdEntry))
            {
                return acdEntry.AcdCode;
            }

            if (TryResolveCurrencyByText(normalized, out var textEntry))
            {
                return textEntry.AcdCode;
            }

            return normalized.Length == 3 && normalized.All(char.IsDigit)
                ? normalized
                : string.Empty;
        }

        public static string NormalizeCustomsCode(string value)
        {
            var normalized = NormalizeText(value);
            return normalized.Length == 10 && normalized.All(char.IsDigit)
                ? normalized
                : string.Empty;
        }

        public static string ResolveEnterpriseCustomsCode(Invoice invoice, Exporter exporter)
        {
            return FirstNonEmpty(
                NormalizeCustomsCode(invoice?.ExporterCustomsCode),
                NormalizeCustomsCode(exporter?.CustomsCode));
        }

        public static string NormalizeUnitEnglish(string value)
        {
            return SingleWindowUnitNormalizer.NormalizeEnglish(value);
        }

        public static string NormalizeUnitChinese(string value)
        {
            return SingleWindowUnitNormalizer.NormalizeChinese(value);
        }

        public static string BuildCooGoodsDescription(
            string packQty,
            string packUnit,
            string goodsNameEnglish,
            string goodsNameChinese = "")
        {
            return CustomsCooGoodsDescriptionTemplateCatalog.BuildTemplate(
                packQty,
                packUnit,
                goodsNameEnglish,
                goodsNameChinese);
        }

        public static string BuildCooPackingSummary(string packQty, string packUnit)
        {
            return CustomsCooGoodsDescriptionTemplateCatalog.BuildPackingSummary(packQty, packUnit);
        }

        public static string NormalizeTransportMode(string value)
        {
            string normalized = NormalizeLookupKey(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var state = CurrentReferenceCatalogState;
            if (state.TransportModeLookup.TryGetValue(normalized, out var mapped))
            {
                return mapped;
            }

            return NormalizeUpperText(value);
        }

        public static string NormalizePort(string value)
        {
            string normalized = NormalizeLookupKey(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return CurrentReferenceCatalogState.PortLookup.TryGetValue(normalized, out var mapped)
                ? mapped
                : NormalizeUpperText(value);
        }

        public static string NormalizePhone(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value.Trim()
                .Where(ch => char.IsLetterOrDigit(ch) || ch is '+' or '-' or '(' or ')' or '#')
                .ToArray();
            return new string(chars);
        }

        public static string NormalizeTradeModeCode(string value)
        {
            var normalized = NormalizeText(value);
            if (normalized.Length == 4 && normalized.All(char.IsDigit))
            {
                return normalized;
            }

            return TryResolveAcdTradeMode(value, out var entry)
                ? entry.Code
                : string.Empty;
        }

        public static string NormalizeAcdTradeModeDisplayText(string value)
        {
            if (!TryResolveAcdTradeMode(value, out var entry))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(entry.Description)
                ? $"{entry.Code}：{entry.Name}"
                : $"{entry.Code}：{entry.Name} - {entry.Description}";
        }

        public static string NormalizeCooTradeModeCode(string value)
        {
            return CustomsCooTradeModeCatalog.NormalizeCode(value);
        }

        public static string PreferCooTradeModeCode(string preferredValue, string fallbackValue)
        {
            return CustomsCooTradeModeCatalog.PreferCode(preferredValue, fallbackValue);
        }

        public static string BuildCooTransportDetails(
            string loadPort,
            string unloadPort,
            string transPort,
            string transMeans)
        {
            var parts = new List<string>();
            string normalizedLoadPort = NormalizePort(loadPort);
            string normalizedUnloadPort = NormalizePort(unloadPort);
            string normalizedTransPort = NormalizePort(transPort);
            string normalizedTransMeans = NormalizeTransportMode(transMeans);

            if (!string.IsNullOrWhiteSpace(normalizedLoadPort))
            {
                parts.Add($"FROM {normalizedLoadPort}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedUnloadPort))
            {
                parts.Add($"TO {normalizedUnloadPort}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedTransPort))
            {
                parts.Add($"VIA {normalizedTransPort}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedTransMeans))
            {
                parts.Add(normalizedTransMeans.StartsWith("BY ", StringComparison.OrdinalIgnoreCase)
                    ? normalizedTransMeans
                    : $"BY {normalizedTransMeans}");
            }

            return string.Join(" ", parts);
        }

        public static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        public static bool TryResolveCountry(string value, out CountryCatalogEntry entry)
        {
            return CurrentReferenceCatalogState.CountryLookup.TryGetValue(NormalizeLookupKey(value), out entry);
        }

        private static bool TryResolveAcdCountry(string value, out AcdCountryCatalogEntry entry)
        {
            return CurrentReferenceCatalogState.AcdCountryLookup.TryGetValue(NormalizeLookupKey(value), out entry);
        }

        private static bool TryResolveCurrencyByText(string value, out CurrencyCatalogEntry entry)
        {
            return CurrentReferenceCatalogState.CurrencyTextLookup.TryGetValue(NormalizeLookupKey(value), out entry);
        }

        private static bool TryResolveCurrencyByAcdCode(string value, out CurrencyCatalogEntry entry)
        {
            return CurrentReferenceCatalogState.CurrencyAcdCodeLookup.TryGetValue(NormalizeLookupKey(value), out entry);
        }

        private static bool TryResolveAcdTradeMode(string value, out AcdTradeModeCatalogEntry entry)
        {
            return CurrentReferenceCatalogState.AcdTradeModeLookup.TryGetValue(NormalizeLookupKey(value), out entry);
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
    }
}

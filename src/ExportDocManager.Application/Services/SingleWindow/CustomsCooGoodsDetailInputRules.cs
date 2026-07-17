using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooGoodsDetailInputRules
    {
        public static string NormalizeInput(
            string propertyName,
            string value,
            string currentOriginCriteria,
            string currentHsCode,
            string certType)
        {
            string normalizedValue = value?.Trim() ?? string.Empty;

            return propertyName switch
            {
                "GoodsItemFlag"
                    => CustomsCooGoodsItemFlagCatalog.NormalizeOrDefault(normalizedValue),
                "PackType"
                    => CustomsCooPackTypeCatalog.NormalizeOrDefault(normalizedValue),
                "OriCriteria"
                    => ResolveSelectionValue(
                        CustomsCooOriginCriteriaCatalog.GetOriginCriteriaOptions(certType),
                        normalizedValue),
                "OriCriteriaSub"
                    => CustomsCooOriginCriteriaCatalog.NormalizeOriginCriteriaSubInput(
                        certType,
                        currentOriginCriteria,
                        ResolveSelectionValue(
                            CustomsCooOriginCriteriaCatalog.GetOriginCriteriaSubOptions(certType, currentOriginCriteria),
                            normalizedValue)),
                "OriCriteriaRef"
                    => CustomsCooOriginCriteriaCatalog.NormalizeOriginCriteriaRefInput(certType, currentOriginCriteria, currentHsCode, normalizedValue),
                _ => normalizedValue
            };
        }

        public static string NormalizeOptionKeyPart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        public static string NormalizeStaleSelectionValue(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Contains("SelectionOption", StringComparison.Ordinal)
                ? string.Empty
                : normalized;
        }

        private static string ResolveSelectionValue(IEnumerable<SelectionOption<string>> options, string value)
        {
            string normalized = NormalizeStaleSelectionValue(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string codePrefix = ResolveSelectionCodePrefix(normalized);
            foreach (var option in options ?? Array.Empty<SelectionOption<string>>())
            {
                if (option == null)
                {
                    continue;
                }

                string optionValue = option.Value?.Trim() ?? string.Empty;
                string optionText = option.Text?.Trim() ?? string.Empty;
                if (string.Equals(optionValue, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(optionText, normalized, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(codePrefix) &&
                     string.Equals(optionValue, codePrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    return optionValue;
                }
            }

            return normalized;
        }

        private static string ResolveSelectionCodePrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int separatorIndex = value.IndexOf('：');
            if (separatorIndex < 0)
            {
                separatorIndex = value.IndexOf(':');
            }

            return separatorIndex <= 0
                ? string.Empty
                : value[..separatorIndex].Trim();
        }
    }
}

using System.Text;

namespace ExportDocManager.Services.Data
{
    internal static class ExcelImportPartyTextClassifier
    {
        private static readonly HashSet<string> CompanyNameMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            "ltd", "limited", "llc", "inc", "corp", "corporation", "company", "group"
        };

        private static readonly HashSet<string> FullAddressMarkers = new(StringComparer.Ordinal)
        {
            "road", "street", "avenue", "boulevard", "building", "floor", "suite", "room",
            "district", "province", "city", "postcode", "zipcode", "china", "usa", "netherlands",
            "kingdom"
        };

        private static readonly HashSet<string> AbbreviatedAddressMarkers = new(StringComparer.Ordinal)
        {
            "rd", "st", "ave", "blvd", "fl", "no"
        };

        private static readonly HashSet<string> ContactMarkers = new(StringComparer.Ordinal)
        {
            "tel", "telephone", "fax", "email", "mail"
        };

        internal static bool LooksLikeCompanyName(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && Tokenize(value.Trim()).Any(CompanyNameMarkers.Contains);
        }

        internal static bool LooksLikePostalAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().ToLowerInvariant();
            var tokens = Tokenize(normalized);
            bool hasNumericToken = tokens.Any(token => token.Any(char.IsDigit));
            bool hasFullMarker = tokens.Any(FullAddressMarkers.Contains);
            bool hasAbbreviatedMarker = hasNumericToken && tokens.Any(AbbreviatedAddressMarkers.Contains);
            bool hasContactMarker = tokens.Any(ContactMarkers.Contains);
            bool hasChineseMarker = normalized.Contains('路')
                || normalized.Contains('街')
                || normalized.Contains('道')
                || normalized.Contains('区')
                || normalized.Contains('省')
                || normalized.Contains('市')
                || (hasNumericToken && normalized.Contains('号'));

            return hasFullMarker
                || hasAbbreviatedMarker
                || hasContactMarker
                || hasChineseMarker
                || (hasNumericToken && (tokens.Count >= 3 || normalized.Contains(',') || normalized.Contains('.')))
                || normalized.Contains("united states", StringComparison.Ordinal);
        }

        internal static bool LooksLikeBusinessPartyValue(string value)
        {
            return LooksLikeCompanyName(value) || LooksLikePostalAddress(value);
        }

        internal static bool IsPlausiblePartyName(string value)
        {
            return HasLetter(value)
                && (LooksLikeCompanyName(value) || !LooksLikePostalAddress(value));
        }

        internal static bool IsPlausiblePartyAddress(string value)
        {
            return HasLetter(value);
        }

        internal static decimal GetFieldQuality(string fieldKey, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0m;
            }

            if (fieldKey is "ExporterNameEN" or "CustomerNameEN" or "NotifyPartyName")
            {
                if (!IsPlausiblePartyName(value))
                {
                    return 0m;
                }

                if (LooksLikeCompanyName(value))
                {
                    return 1m;
                }

                return LooksLikePostalAddress(value) ? 0.1m : 0.75m;
            }

            if (fieldKey is "ExporterAddressEN" or "CustomerAddressEN" or "NotifyPartyAddress")
            {
                if (!IsPlausiblePartyAddress(value))
                {
                    return 0m;
                }

                if (LooksLikePostalAddress(value))
                {
                    return 1m;
                }

                return LooksLikeCompanyName(value) ? 0.2m : 0.55m;
            }

            return 0.8m;
        }

        private static bool HasLetter(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Any(char.IsLetter);
        }

        private static IReadOnlyList<string> Tokenize(string value)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    token.Append(character);
                    continue;
                }

                AddToken(tokens, token);
            }

            AddToken(tokens, token);
            return tokens;
        }

        private static void AddToken(List<string> tokens, StringBuilder token)
        {
            if (token.Length == 0)
            {
                return;
            }

            tokens.Add(token.ToString());
            token.Clear();
        }
    }
}

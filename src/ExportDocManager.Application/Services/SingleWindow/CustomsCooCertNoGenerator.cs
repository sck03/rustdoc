using System.Globalization;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooCertNoGenerator
    {
        private static readonly Regex StructuredPattern = new(
            @"^(?<type>[A-Za-z]{1,2})(?<year>\d{2})(?<org>[A-Za-z0-9]{9})(?<seq>\d{4})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string NormalizeOrGenerate(
            string currentValue,
            string certType,
            string primaryEnterpriseCode,
            string secondaryEnterpriseCode,
            string aplDate,
            bool generateWhenBlank = true)
        {
            string normalizedCurrent = NormalizeText(currentValue);

            if (string.IsNullOrWhiteSpace(normalizedCurrent))
            {
                return generateWhenBlank && TryBuildPrefix(certType, primaryEnterpriseCode, secondaryEnterpriseCode, aplDate, out string prefix)
                    ? prefix + "0001"
                    : string.Empty;
            }

            if (normalizedCurrent.Length == 4 && IsDigits(normalizedCurrent))
            {
                return TryBuildPrefix(certType, primaryEnterpriseCode, secondaryEnterpriseCode, aplDate, out string prefix)
                    ? prefix + normalizedCurrent
                    : normalizedCurrent;
            }

            if (TryGetStructuredSequence(normalizedCurrent, out string sequence) &&
                TryBuildPrefix(certType, primaryEnterpriseCode, secondaryEnterpriseCode, aplDate, out string structuredPrefix))
            {
                return structuredPrefix + sequence;
            }

            return normalizedCurrent;
        }

        public static bool TryBuildPrefix(
            string certType,
            string primaryEnterpriseCode,
            string secondaryEnterpriseCode,
            string aplDate,
            out string prefix)
        {
            prefix = string.Empty;

            string normalizedType = NormalizeCertType(certType);
            string normalizedOrgCode = ResolveOrganizationCodeSegment(primaryEnterpriseCode, secondaryEnterpriseCode);
            if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(normalizedOrgCode))
            {
                return false;
            }

            prefix = normalizedType + ResolveYear(aplDate) + normalizedOrgCode;
            return true;
        }

        public static bool TryGetStructuredSequence(string certNo, out string sequence)
        {
            sequence = string.Empty;
            string normalized = NormalizeText(certNo);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var match = StructuredPattern.Match(normalized);
            if (!match.Success)
            {
                return false;
            }

            sequence = match.Groups["seq"].Value;
            return true;
        }

        private static string ResolveYear(string aplDate)
        {
            if (DateTime.TryParseExact(
                    NormalizeText(aplDate),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime exactDate))
            {
                return exactDate.ToString("yy", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(aplDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate.ToString("yy", CultureInfo.InvariantCulture);
            }

            return DateTime.Today.ToString("yy", CultureInfo.InvariantCulture);
        }

        private static string NormalizeCertType(string certType)
        {
            string normalized = NormalizeText(certType).ToUpperInvariant();
            return normalized.Length is >= 1 and <= 2 ? normalized : string.Empty;
        }

        private static string ResolveOrganizationCodeSegment(string primaryEnterpriseCode, string secondaryEnterpriseCode)
        {
            foreach (string candidate in new[] { primaryEnterpriseCode, secondaryEnterpriseCode })
            {
                string normalized = NormalizeText(candidate).ToUpperInvariant();
                if (normalized.Length == 18)
                {
                    string segment = normalized.Substring(8, 9);
                    if (IsAlphaNumeric(segment))
                    {
                        return segment;
                    }
                }

                if (normalized.Length == 9 && IsAlphaNumeric(normalized))
                {
                    return normalized;
                }
            }

            return string.Empty;
        }

        private static bool IsDigits(string value) => value.All(char.IsDigit);
        private static bool IsAlphaNumeric(string value) => value.All(char.IsLetterOrDigit);

        private static string NormalizeText(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}

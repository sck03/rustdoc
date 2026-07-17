using ExportDocManager.Models.Entities;

namespace ExportDocManager.Utils
{
    public static class HsCodeTextHelper
    {
        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(code.Length);
            foreach (var character in code.Trim())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }

            return builder.ToString();
        }

        public static string NormalizeCodeSearchKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return string.Empty;
            }

            var trimmedKeyword = keyword.Trim();
            var digitCount = 0;

            foreach (var character in trimmedKeyword)
            {
                if (char.IsDigit(character))
                {
                    digitCount++;
                    continue;
                }

                if (char.IsLetter(character) ||
                    char.IsWhiteSpace(character) ||
                    character == '.' ||
                    character == '．' ||
                    character == '-' ||
                    character == '_' ||
                    character == '/')
                {
                    continue;
                }

                return string.Empty;
            }

            return digitCount >= 4
                ? NormalizeCode(trimmedKeyword)
                : string.Empty;
        }

        public static bool HasSameCode(string left, string right)
        {
            var normalizedLeft = NormalizeCode(left);
            var normalizedRight = NormalizeCode(right);

            return !string.IsNullOrWhiteSpace(normalizedLeft) &&
                   string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsExpired(HsCode hsCode)
        {
            return hsCode != null &&
                   (IsExpiredText(hsCode.Name) || IsExpiredText(hsCode.Code));
        }

        public static bool IsExpiredText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains("（已作废）", StringComparison.Ordinal) ||
                   value.Contains("(已作废)", StringComparison.Ordinal) ||
                   value.Contains("已作废", StringComparison.Ordinal);
        }
    }
}

using System.Globalization;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowFieldValidationHelper
    {
        public static readonly IReadOnlyList<string> CooTradeModeCodeValues =
        [
            "0",
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "10",
            "33",
            "34"
        ];

        public static string FormatDecimal(decimal value, int decimals)
        {
            if (value == 0)
            {
                return string.Empty;
            }

            return Math.Round(value, decimals).ToString($"F{decimals}", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }

        public static void ValidateMaxLength(string value, int maxLength, string fieldName, ICollection<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
            {
                errors.Add($"{fieldName}长度不能超过 {maxLength}。");
            }
        }

        public static void ValidateExactDate(string value, string format, string fieldName, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!DateTime.TryParseExact(
                    value.Trim(),
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                errors.Add($"{fieldName}格式无效，应为 {format}。");
            }
        }

        public static void ValidateDecimal(string value, int maxIntegerDigits, int maxScale, string fieldName, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = value.Trim();
            if (!decimal.TryParse(
                    normalized,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                errors.Add($"{fieldName}不是有效数值。");
                return;
            }

            string unsigned = normalized.TrimStart('+', '-');
            string[] parts = unsigned.Split('.', 2);
            int integerDigits = parts[0].TrimStart('0').Length;
            if (parts[0].All(ch => ch == '0'))
            {
                integerDigits = 0;
            }

            int scale = parts.Length == 2 ? parts[1].Length : 0;
            if (integerDigits > maxIntegerDigits || scale > maxScale)
            {
                errors.Add($"{fieldName}超过允许精度，整数位不能超过 {maxIntegerDigits}，小数位不能超过 {maxScale}。");
            }
        }

        public static void ValidatePercentText(
            string value,
            decimal maxPercent,
            int maxScale,
            string fieldName,
            ICollection<string> errors,
            bool enabled = true)
        {
            if (!enabled || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = value.Trim();
            string core = normalized.EndsWith("%", StringComparison.Ordinal)
                ? normalized[..^1].Trim()
                : normalized;
            if (!decimal.TryParse(
                    core,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var percentage))
            {
                errors.Add($"{fieldName}格式无效，应为 0-100% 之间的百分比。");
                return;
            }

            if (percentage < 0 || percentage > maxPercent)
            {
                errors.Add($"{fieldName}超出允许范围，应在 0% 到 {maxPercent:0.##}% 之间。");
                return;
            }

            string[] parts = core.Split('.', 2);
            int scale = parts.Length == 2 ? parts[1].Length : 0;
            if (scale > maxScale)
            {
                errors.Add($"{fieldName}小数位不能超过 {maxScale} 位。");
            }
        }

        public static void ValidateDigits(string value, int exactLength, string fieldName, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = value.Trim();
            if (normalized.Length != exactLength || !normalized.All(char.IsDigit))
            {
                errors.Add($"{fieldName}应为 {exactLength} 位数字。");
            }
        }

        public static void ValidateAllowedValues(string value, IReadOnlyList<string> allowedValues, string fieldName, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!allowedValues.Contains(value.Trim(), StringComparer.Ordinal))
            {
                errors.Add($"{fieldName}取值无效，应为 {string.Join("/", allowedValues)}。");
            }
        }

        public static void AddIfMissing(string value, string message, ICollection<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                warnings.Add(message);
            }
        }

        public static void RequireValue(string value, string fieldName, ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{fieldName}不能为空。");
            }
        }

        public static string PreferValue(string preferredValue, string fallbackValue)
        {
            return string.IsNullOrWhiteSpace(preferredValue)
                ? fallbackValue
                : preferredValue.Trim();
        }
    }
}

namespace ExportDocManager.Services.SingleWindow
{
    public static partial class SingleWindowFieldMapperHelpers
    {
        public static string FormatDecimal(decimal value, int decimals) =>
            SingleWindowFieldValidationHelper.FormatDecimal(value, decimals);

        public static void ValidateMaxLength(string value, int maxLength, string fieldName, ICollection<string> errors) =>
            SingleWindowFieldValidationHelper.ValidateMaxLength(value, maxLength, fieldName, errors);

        public static void ValidateExactDate(string value, string format, string fieldName, ICollection<string> errors) =>
            SingleWindowFieldValidationHelper.ValidateExactDate(value, format, fieldName, errors);

        public static void ValidateDecimal(string value, int maxIntegerDigits, int maxScale, string fieldName, ICollection<string> errors) =>
            SingleWindowFieldValidationHelper.ValidateDecimal(value, maxIntegerDigits, maxScale, fieldName, errors);

        public static void ValidatePercentText(
            string value,
            decimal maxPercent,
            int maxScale,
            string fieldName,
            ICollection<string> errors,
            bool enabled = true) =>
            SingleWindowFieldValidationHelper.ValidatePercentText(value, maxPercent, maxScale, fieldName, errors, enabled);

        public static void ValidateDigits(string value, int exactLength, string fieldName, ICollection<string> errors) =>
            SingleWindowFieldValidationHelper.ValidateDigits(value, exactLength, fieldName, errors);

        public static void ValidateAllowedValues(string value, IReadOnlyList<string> allowedValues, string fieldName, ICollection<string> errors) =>
            SingleWindowFieldValidationHelper.ValidateAllowedValues(value, allowedValues, fieldName, errors);

        public static void AddIfMissing(string value, string message, ICollection<string> warnings) =>
            SingleWindowFieldValidationHelper.AddIfMissing(value, message, warnings);

        public static void RequireValue(string value, string fieldName, ICollection<string> errors) =>
            SingleWindowFieldValidationHelper.RequireValue(value, fieldName, errors);

        public static string PreferValue(string preferredValue, string fallbackValue) =>
            SingleWindowFieldValidationHelper.PreferValue(preferredValue, fallbackValue);
    }
}

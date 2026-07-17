namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooPackTypeCatalog
    {
        public const string RegularCode = "1";
        public const string IrregularCode = "2";
        public const string DefaultCode = RegularCode;

        public static IReadOnlyList<string> AllowedCodes { get; } =
        [
            RegularCode,
            IrregularCode
        ];

        public static string NormalizeOrDefault(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return string.Equals(normalized, RegularCode, StringComparison.Ordinal) ||
                   string.Equals(normalized, IrregularCode, StringComparison.Ordinal)
                ? normalized
                : DefaultCode;
        }

        public static bool IsIrregular(string value)
        {
            return string.Equals(NormalizeOrDefault(value), IrregularCode, StringComparison.Ordinal);
        }
    }
}

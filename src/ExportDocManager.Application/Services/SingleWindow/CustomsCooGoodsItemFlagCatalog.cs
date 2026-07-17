namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooGoodsItemFlagCatalog
    {
        public const string GoodsCode = "N";
        public const string NonGoodsCode = "Y";
        public const string DefaultCode = GoodsCode;

        public static IReadOnlyList<string> AllowedCodes { get; } =
        [
            GoodsCode,
            NonGoodsCode
        ];

        public static string NormalizeOrDefault(string value)
        {
            string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
            return string.Equals(normalized, GoodsCode, StringComparison.Ordinal) ||
                   string.Equals(normalized, NonGoodsCode, StringComparison.Ordinal)
                ? normalized
                : DefaultCode;
        }

        public static bool IsNonGoods(string value)
        {
            return string.Equals(NormalizeOrDefault(value), NonGoodsCode, StringComparison.Ordinal);
        }
    }
}

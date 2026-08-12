namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Defines which price value is authoritative for an invoice item.
    /// 定义发票明细当前以单价还是行金额作为核算依据。
    /// </summary>
    public static class ItemPriceCalculationModeCatalog
    {
        public const string UnitPriceDriven = "UnitPriceDriven";
        public const string LineAmountDriven = "LineAmountDriven";

        public static bool IsKnown(string? value) =>
            string.Equals(value, UnitPriceDriven, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, LineAmountDriven, StringComparison.OrdinalIgnoreCase);

        public static string Normalize(string? value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, UnitPriceDriven, StringComparison.OrdinalIgnoreCase))
            {
                return UnitPriceDriven;
            }

            return string.Equals(normalized, LineAmountDriven, StringComparison.OrdinalIgnoreCase)
                ? LineAmountDriven
                : normalized;
        }
    }
}

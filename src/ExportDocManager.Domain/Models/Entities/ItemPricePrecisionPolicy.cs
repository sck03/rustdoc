using System.Globalization;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Shared precision rules for invoice selling prices.
    /// 普通单价按 2 位展示；只有 2 位无法准确表达时，最多展示/存储 5 位。
    /// </summary>
    public static class ItemPricePrecisionPolicy
    {
        public const int StandardDisplayScale = 2;
        public const int MaximumScale = 5;

        public static decimal Round(decimal value) =>
            decimal.Round(value, MaximumScale, MidpointRounding.AwayFromZero);

        public static bool NeedsExtendedDisplay(decimal value)
        {
            decimal normalized = Round(value);
            return decimal.Round(normalized, StandardDisplayScale, MidpointRounding.AwayFromZero) != normalized;
        }

        /// <summary>
        /// Formats a unit price without thousands separators so the value can be
        /// reused in HTML number inputs and exported text fields.
        /// </summary>
        public static string Format(decimal value)
        {
            decimal normalized = Round(value);
            int scale = NeedsExtendedDisplay(normalized) ? MaximumScale : StandardDisplayScale;
            return normalized.ToString($"F{scale}", CultureInfo.InvariantCulture);
        }
    }
}

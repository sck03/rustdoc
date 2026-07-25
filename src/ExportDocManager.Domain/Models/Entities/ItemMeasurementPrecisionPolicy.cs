using System.Globalization;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Shared precision rules for invoice packing measurements.
    /// 发票明细中的毛重/净重统一保留 2 位，体积统一保留 3 位。
    /// </summary>
    public static class ItemMeasurementPrecisionPolicy
    {
        public const int WeightScale = 2;
        public const int VolumeScale = 3;

        public static decimal RoundWeight(decimal value) =>
            decimal.Round(value, WeightScale, MidpointRounding.AwayFromZero);

        public static decimal RoundVolume(decimal value) =>
            decimal.Round(value, VolumeScale, MidpointRounding.AwayFromZero);

        public static string FormatWeight(decimal value) =>
            RoundWeight(value).ToString($"F{WeightScale}", CultureInfo.InvariantCulture);

        public static string FormatVolume(decimal value) =>
            RoundVolume(value).ToString($"F{VolumeScale}", CultureInfo.InvariantCulture);
    }
}

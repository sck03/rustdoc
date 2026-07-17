namespace ExportDocManager.Models.DTOs
{
    public readonly record struct ContainerPackingColor(byte A, byte R, byte G, byte B)
    {
        public static ContainerPackingColor FromArgb(int argb)
        {
            unchecked
            {
                return new ContainerPackingColor(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
            }
        }

        public static ContainerPackingColor FromArgb(byte a, byte r, byte g, byte b)
        {
            return new ContainerPackingColor(a, r, g, b);
        }

        public static ContainerPackingColor FromRgb(byte r, byte g, byte b)
        {
            return new ContainerPackingColor(255, r, g, b);
        }

        public int ToArgb()
        {
            unchecked
            {
                return (A << 24) | (R << 16) | (G << 8) | B;
            }
        }
    }

    public sealed record ContainerPackingCargoInput(
        string Name,
        decimal Length,
        decimal Width,
        decimal Height,
        decimal Weight,
        int Quantity,
        ContainerPackingColor Color,
        bool UsePallet,
        int UnitsPerPallet,
        decimal MaxTopLoadWeight,
        ContainerCargoZone PreferredZone,
        int LoadSequence,
        string PriorityGroup)
    {
        public decimal Volume => (Length * Width * Height / 1000000m) * Quantity;

        public decimal TotalWeight => Weight * Quantity;
    }

    public sealed record ContainerPackingRequest(
        ContainerDimensions Container,
        IReadOnlyList<ContainerPackingCargoInput> CargoItems,
        ContainerPackingRules Rules);

    public sealed record ContainerPackingAnalysis(
        IReadOnlyList<PackedCargoItem> PackedItems,
        int TotalPackages,
        int PackedPackages,
        int UnpackedPackages,
        int TotalPallets,
        int PackedPallets,
        decimal TotalVolume,
        decimal TotalWeight,
        decimal PackedVolume,
        decimal PackedWeight,
        decimal VolumeUtilizationPercent,
        decimal WeightUtilizationPercent,
        int ContainersNeededByVolume,
        int ContainersNeededByWeight,
        decimal CenterOfGravityX,
        decimal CenterOfGravityY,
        decimal CenterOfGravityLengthDeviationPercent,
        decimal CenterOfGravityWidthDeviationPercent,
        bool IsCenterOfGravityWithinTolerance)
    {
        public int EstimatedContainerCount => Math.Max(ContainersNeededByVolume, ContainersNeededByWeight);
    }

    public sealed record PackedCargoItem(
        float X,
        float Y,
        float Width,
        float Height,
        float BaseHeight,
        float OccupiedHeight,
        ContainerPackingColor Color,
        int UnitsRepresented,
        int LoadCount,
        string DisplayText,
        string DetailText,
        bool IsRotated,
        bool IsPalletized,
        string Name,
        decimal TotalWeight,
        string PriorityGroup,
        ContainerCargoZone PreferredZone)
    {
        public int StackCount => UnitsRepresented;

        public float TopHeight => BaseHeight + OccupiedHeight;
    }
}

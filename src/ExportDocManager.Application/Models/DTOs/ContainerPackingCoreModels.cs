namespace ExportDocManager.Models.DTOs
{
    public enum ContainerCargoZone
    {
        Auto = 0,
        Head = 1,
        Middle = 2,
        Door = 3
    }

    public enum ContainerPackingRenderMode
    {
        OutlineOnly = 0,
        FullGrid = 1
    }

    public static class ContainerPackingDisplayText
    {
        public static string GetZoneText(ContainerCargoZone zone)
        {
            return zone switch
            {
                ContainerCargoZone.Head => "柜头段",
                ContainerCargoZone.Middle => "中段",
                ContainerCargoZone.Door => "柜门段",
                _ => "自动"
            };
        }

        public static string GetRenderModeText(ContainerPackingRenderMode mode)
        {
            return mode switch
            {
                ContainerPackingRenderMode.FullGrid => "完整分格",
                _ => "仅外轮廓"
            };
        }
    }

    public readonly record struct ContainerDimensions(int Length, int Width, int Height, decimal Volume, decimal MaxWeight);

    public sealed record ContainerPackingRules(
        bool AllowRotation,
        bool UsePalletConstraints,
        int DefaultPalletLength,
        int DefaultPalletWidth,
        int DefaultPalletHeight,
        decimal DefaultPalletWeight,
        bool EnforceCenterOfGravity,
        decimal CenterOfGravityTolerancePercent,
        decimal MinimumSupportAreaPercent,
        bool RequireSameFootprintStacking);
}

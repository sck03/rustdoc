using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Application.Tests
{
    public class ContainerPackingContractTests
    {
        [Theory]
        [InlineData(ContainerCargoZone.Auto, "自动")]
        [InlineData(ContainerCargoZone.Head, "柜头段")]
        [InlineData(ContainerCargoZone.Middle, "中段")]
        [InlineData(ContainerCargoZone.Door, "柜门段")]
        public void DisplayText_ShouldKeepZoneNames(ContainerCargoZone zone, string expected)
        {
            Assert.Equal(expected, ContainerPackingDisplayText.GetZoneText(zone));
        }

        [Fact]
        public void DisplayText_ShouldKeepRenderModeNames()
        {
            Assert.Equal("仅外轮廓", ContainerPackingDisplayText.GetRenderModeText(ContainerPackingRenderMode.OutlineOnly));
            Assert.Equal("完整分格", ContainerPackingDisplayText.GetRenderModeText(ContainerPackingRenderMode.FullGrid));
        }

        [Fact]
        public void ContainerDimensions_ShouldKeepValueSemantics()
        {
            var first = new ContainerDimensions(1200, 235, 239, 67.4m, 28000m);
            var second = new ContainerDimensions(1200, 235, 239, 67.4m, 28000m);

            Assert.Equal(first, second);
            Assert.Equal(1200, first.Length);
            Assert.Equal(28000m, first.MaxWeight);
        }

        [Fact]
        public void ContainerPackingRules_ShouldKeepConstructorOrder()
        {
            var rules = new ContainerPackingRules(
                AllowRotation: true,
                UsePalletConstraints: false,
                DefaultPalletLength: 120,
                DefaultPalletWidth: 100,
                DefaultPalletHeight: 15,
                DefaultPalletWeight: 12.5m,
                EnforceCenterOfGravity: true,
                CenterOfGravityTolerancePercent: 8m,
                MinimumSupportAreaPercent: 70m,
                RequireSameFootprintStacking: true);

            Assert.True(rules.AllowRotation);
            Assert.False(rules.UsePalletConstraints);
            Assert.Equal(120, rules.DefaultPalletLength);
            Assert.True(rules.RequireSameFootprintStacking);
        }

        [Fact]
        public void ContainerPackingColor_ShouldRoundTripArgbWithoutSystemDrawingDependency()
        {
            var color = ContainerPackingColor.FromArgb(unchecked((int)0xCC4287F5));

            Assert.Equal(0xCC, color.A);
            Assert.Equal(0x42, color.R);
            Assert.Equal(0x87, color.G);
            Assert.Equal(0xF5, color.B);
            Assert.Equal(unchecked((int)0xCC4287F5), color.ToArgb());
        }
    }
}

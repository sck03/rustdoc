using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooGoodsDescriptionTemplateApplicationTests
    {
        [Fact]
        public void BuildTemplate_ShouldBuildNudePackGoodsDescription()
        {
            string description = CustomsCooGoodsDescriptionTemplateCatalog.BuildTemplate(
                "11",
                "PCS IN NUDE",
                "T SHIRT");

            Assert.Equal("ELEVEN (11) PCS IN NUDE OF T SHIRT", description);
        }

        [Theory]
        [InlineData("", "PCS IN NUDE")]
        [InlineData("11", "")]
        [InlineData("11", "PCS IN NUDE", "")]
        public void BuildTemplate_ShouldRequirePackingSummaryAndGoodsName(
            string packQty,
            string packUnit,
            string goodsNameEnglish = "T SHIRT")
        {
            string description = CustomsCooGoodsDescriptionTemplateCatalog.BuildTemplate(
                packQty,
                packUnit,
                goodsNameEnglish);

            Assert.Equal(string.Empty, description);
        }

        [Fact]
        public void ResolveActionState_ShouldKeepIrregularPackGuidance()
        {
            var state = CustomsCooGoodsDescriptionTemplateCatalog.ResolveActionState(
                CustomsCooGoodsItemFlagCatalog.GoodsCode,
                CustomsCooPackTypeCatalog.IrregularCode);

            Assert.True(state.IsEnabled);
            Assert.Equal("生成货物描述", state.ButtonText);
            Assert.Contains("包装单位/形式", state.ToolTipText, StringComparison.Ordinal);
        }
    }
}

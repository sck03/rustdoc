using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooEditorGuidanceCatalogApplicationTests
    {
        [Fact]
        public void GetHeaderCueText_ShouldReturnIssuingAuthorityCue()
        {
            string cueText = CustomsCooEditorGuidanceCatalog.GetHeaderCueText("OrgCode");

            Assert.Contains("4 位关区代码", cueText, StringComparison.Ordinal);
            Assert.Contains("3101", cueText, StringComparison.Ordinal);
        }

        [Fact]
        public void GetHeaderToolTip_ShouldWarnCooTradeModeUsesCertificateCodes()
        {
            string toolTip = CustomsCooEditorGuidanceCatalog.GetHeaderToolTip("TradeModeCode");

            Assert.Contains("产地证贸易方式代码", toolTip, StringComparison.Ordinal);
            Assert.Contains("0110", toolTip, StringComparison.Ordinal);
        }

        [Fact]
        public void GetGoodsDetailGuidance_ShouldDescribeNonGoodsItemMode()
        {
            string labelText = CustomsCooEditorGuidanceCatalog.GetGoodsDetailLabelText(
                "GoodsName",
                "中文名",
                goodsItemFlag: "Y",
                packType: "1");
            string cueText = CustomsCooEditorGuidanceCatalog.GetGoodsDetailCueText(
                "GoodsNameE",
                certType: "RC",
                originCriteria: string.Empty,
                goodsItemFlag: "Y");
            string summary = CustomsCooEditorGuidanceCatalog.GetGoodsDetailContextSummary("Y", "1");

            Assert.Equal("中文性质/名称(只读)", labelText);
            Assert.Contains("英文性质/名称", cueText, StringComparison.Ordinal);
            Assert.Contains("当前货项为非货物项", summary, StringComparison.Ordinal);
        }

        [Fact]
        public void GetGoodsDetailGuidance_ShouldDescribeIrregularPackMode()
        {
            string labelText = CustomsCooEditorGuidanceCatalog.GetGoodsDetailLabelText(
                "PackUnit",
                "包装单位(英)",
                goodsItemFlag: "N",
                packType: "2");
            string cueText = CustomsCooEditorGuidanceCatalog.GetGoodsDetailCueText(
                "GoodsDesc",
                certType: "C",
                originCriteria: string.Empty,
                goodsItemFlag: "N",
                packType: "2");

            Assert.Equal("包装单位/形式(英)", labelText);
            Assert.Contains("HANGING GARMENT", cueText, StringComparison.Ordinal);
        }
    }
}

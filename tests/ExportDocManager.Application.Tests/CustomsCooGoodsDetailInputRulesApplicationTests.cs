using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooGoodsDetailInputRulesApplicationTests
    {
        [Theory]
        [InlineData("WO：完全获得或生产", "WO")]
        [InlineData("pe", "PE")]
        [InlineData("SelectionOption`1", "")]
        public void NormalizeInput_ShouldResolveOriginCriteriaSelectionText(string input, string expected)
        {
            string actual = CustomsCooGoodsDetailInputRules.NormalizeInput(
                nameof(CustomsCooItem.OriCriteria),
                input,
                currentOriginCriteria: string.Empty,
                currentHsCode: string.Empty,
                certType: "E");

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("A", "1")]
        [InlineData("2：适用归类改变标准（CTC）", "2")]
        public void NormalizeInput_ShouldResolveFormEOriginCriteriaSubAliases(string input, string expected)
        {
            string actual = CustomsCooGoodsDetailInputRules.NormalizeInput(
                nameof(CustomsCooItem.OriCriteriaSub),
                input,
                currentOriginCriteria: "PSR",
                currentHsCode: string.Empty,
                certType: "E");

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void NormalizeInput_ShouldFormatPercentOriginCriteriaRef()
        {
            string actual = CustomsCooGoodsDetailInputRules.NormalizeInput(
                nameof(CustomsCooItem.OriCriteriaRef),
                "40",
                currentOriginCriteria: "WO",
                currentHsCode: string.Empty,
                certType: "E");

            Assert.Equal("40%", actual);
        }

        [Fact]
        public void NormalizeInput_ShouldUseHsHeadingForGspWOriginCriteriaRef()
        {
            string actual = CustomsCooGoodsDetailInputRules.NormalizeInput(
                nameof(CustomsCooItem.OriCriteriaRef),
                "manual",
                currentOriginCriteria: "W",
                currentHsCode: "6109.90.1000",
                certType: "G");

            Assert.Equal("61.09", actual);
        }
    }
}

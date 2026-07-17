using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowFieldTransformApplicationTests
    {
        [Theory]
        [InlineData("件", "PCS")]
        [InlineData("cartons", "CTN")]
        [InlineData("kg", "KGS")]
        [InlineData("pkg", "PKG")]
        public void SingleWindowUnitNormalizer_ShouldNormalizeEnglishUnit(string input, string expected)
        {
            Assert.Equal(expected, SingleWindowUnitNormalizer.NormalizeEnglish(input));
        }

        [Theory]
        [InlineData("PCS", "件")]
        [InlineData("box", "盒")]
        [InlineData("公斤", "千克")]
        [InlineData("包", "包")]
        public void SingleWindowUnitNormalizer_ShouldNormalizeChineseUnit(string input, string expected)
        {
            Assert.Equal(expected, SingleWindowUnitNormalizer.NormalizeChinese(input));
        }

        [Theory]
        [InlineData("一般贸易", "1")]
        [InlineData("边境小额贸易", "34")]
        [InlineData("34", "34")]
        [InlineData("未知贸易方式", "")]
        public void CustomsCooTradeModeCatalog_ShouldNormalizeTradeModeCode(string input, string expected)
        {
            Assert.Equal(expected, CustomsCooTradeModeCatalog.NormalizeCode(input));
        }

        [Fact]
        public void CustomsCooTradeModeCatalog_ShouldPreferRecognizedPreferredCode()
        {
            Assert.Equal("2", CustomsCooTradeModeCatalog.PreferCode("2", "一般贸易"));
            Assert.Equal("1", CustomsCooTradeModeCatalog.PreferCode("未识别", "一般贸易"));
        }
    }
}

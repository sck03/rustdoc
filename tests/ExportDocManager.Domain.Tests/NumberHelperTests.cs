using ExportDocManager.Utils;

namespace ExportDocManager.Domain.Tests
{
    public class NumberHelperTests
    {
        [Theory]
        [InlineData("12.34", "12.34")]
        [InlineData("", "0")]
        [InlineData("not-a-number", "0")]
        public void ParseDecimal_ShouldKeepFallbackBehavior(string value, string expected)
        {
            Assert.Equal(decimal.Parse(expected), NumberHelper.ParseDecimal(value));
        }

        [Theory]
        [InlineData("42", 42)]
        [InlineData("", 0)]
        [InlineData("x", 0)]
        public void ParseInt_ShouldKeepFallbackBehavior(string value, int expected)
        {
            Assert.Equal(expected, NumberHelper.ParseInt(value));
        }

        [Theory]
        [InlineData(0, "ZERO")]
        [InlineData(125.50, "ONE HUNDRED AND TWENTY-FIVE AND CENTS FIFTY")]
        [InlineData(-7, "MINUS SEVEN")]
        public void ToEnglishWords_ShouldKeepReportText(decimal value, string expected)
        {
            Assert.Equal(expected, NumberHelper.ToEnglishWords(value));
        }

        [Theory]
        [InlineData(0, "零元整")]
        [InlineData(10.05, "壹拾元零伍分")]
        [InlineData(125.50, "壹佰贰拾伍元伍角")]
        public void ToChineseMoney_ShouldKeepMoneyText(decimal value, string expected)
        {
            Assert.Equal(expected, NumberHelper.ToChineseMoney(value));
        }
    }
}

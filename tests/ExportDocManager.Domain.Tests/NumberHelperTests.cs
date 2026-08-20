using ExportDocManager.Utils;
using System.Globalization;

namespace ExportDocManager.Domain.Tests
{
    public class NumberHelperTests
    {
        [Theory]
        [InlineData("12.34", "12.34")]
        [InlineData(" 12.34 ", "12.34")]
        [InlineData("", "0")]
        [InlineData("not-a-number", "0")]
        public void ParseDecimal_ShouldKeepFallbackBehavior(string value, string expected)
        {
            Assert.Equal(decimal.Parse(expected), NumberHelper.ParseDecimal(value));
        }

        [Fact]
        public void NumericParsing_ShouldRemainInvariantUnderCommaDecimalCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

                Assert.Equal(720.5m, NumberHelper.ParseDecimal("720.5"));
                Assert.Equal(0m, NumberHelper.ParseDecimal("720,5"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
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

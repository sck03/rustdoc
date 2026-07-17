using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Domain.Tests
{
    public class HsCodeTextHelperTests
    {
        [Theory]
        [InlineData(" 6204.62-00 ", "62046200")]
        [InlineData("ab-12 cd", "AB12CD")]
        [InlineData("", "")]
        public void NormalizeCode_ShouldRemoveSeparatorsAndUppercase(string value, string expected)
        {
            Assert.Equal(expected, HsCodeTextHelper.NormalizeCode(value));
        }

        [Theory]
        [InlineData("6204.62", "620462")]
        [InlineData("62", "")]
        [InlineData("税号?", "")]
        public void NormalizeCodeSearchKeyword_ShouldRequireEnoughDigits(string value, string expected)
        {
            Assert.Equal(expected, HsCodeTextHelper.NormalizeCodeSearchKeyword(value));
        }

        [Fact]
        public void HsCode_ShouldMaintainNormalizedCodeWhenCodeChanges()
        {
            var hsCode = new HsCode { Code = "6204.62-00" };

            Assert.Equal("62046200", hsCode.NormalizedCode);
        }

        [Fact]
        public void IsExpired_ShouldDetectExpiredCodeOrNameText()
        {
            Assert.True(HsCodeTextHelper.IsExpired(new HsCode { Code = "6204", Name = "女裤（已作废）" }));
            Assert.False(HsCodeTextHelper.IsExpired(new HsCode { Code = "6204", Name = "女裤" }));
        }
    }
}

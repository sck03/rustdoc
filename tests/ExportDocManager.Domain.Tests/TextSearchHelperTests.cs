using ExportDocManager.Utils;

namespace ExportDocManager.Domain.Tests
{
    public class TextSearchHelperTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("   ", "")]
        [InlineData("  abc  ", "abc")]
        public void NormalizeValue_ShouldTrimAndEmptyNullishInput(string? value, string expected)
        {
            Assert.Equal(expected, TextSearchHelper.NormalizeValue(value));
        }

        [Theory]
        [InlineData("  cn-01  ", "CN-01")]
        [InlineData(null, "")]
        public void NormalizeUpperValue_ShouldTrimAndUppercase(string? value, string expected)
        {
            Assert.Equal(expected, TextSearchHelper.NormalizeUpperValue(value));
        }

        [Fact]
        public void Tokenize_ShouldTrimAndRemoveCaseInsensitiveDuplicates()
        {
            var tokens = TextSearchHelper.Tokenize(" alpha  beta ALPHA ");

            Assert.Equal(["alpha", "beta"], tokens);
        }

    }
}

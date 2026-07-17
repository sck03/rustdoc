using ExportDocManager.Utils;

namespace ExportDocManager.Domain.Tests
{
    public class TextSearchHelperTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("   ", "")]
        [InlineData("  abc  ", "abc")]
        public void NormalizeValue_ShouldTrimAndEmptyNullishInput(string value, string expected)
        {
            Assert.Equal(expected, TextSearchHelper.NormalizeValue(value));
        }

        [Theory]
        [InlineData("  cn-01  ", "CN-01")]
        [InlineData(null, "")]
        public void NormalizeUpperValue_ShouldTrimAndUppercase(string value, string expected)
        {
            Assert.Equal(expected, TextSearchHelper.NormalizeUpperValue(value));
        }

        [Fact]
        public void Tokenize_ShouldTrimAndRemoveCaseInsensitiveDuplicates()
        {
            var tokens = TextSearchHelper.Tokenize(" alpha  beta ALPHA ");

            Assert.Equal(["alpha", "beta"], tokens);
        }

        [Fact]
        public void ApplyKeywordSearch_ShouldMatchAllTokensAcrossConfiguredFields()
        {
            var rows = new[]
            {
                new SearchRow { Code = "SKU-001", Name = "Beta Shirt" },
                new SearchRow { Code = "SKU-002", Name = "Alpha Shirt" },
                new SearchRow { Code = "SKU-003", Name = null }
            }.AsQueryable();

            var result = rows
                .ApplyKeywordSearch(" alpha 002 ", row => row.Code, row => row.Name)
                .ToList();

            var matched = Assert.Single(result);
            Assert.Equal("SKU-002", matched.Code);
        }

        private sealed class SearchRow
        {
            public string Code { get; init; }

            public string Name { get; init; }
        }
    }
}

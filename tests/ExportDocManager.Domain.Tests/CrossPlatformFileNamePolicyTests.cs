using ExportDocManager.Utils;

namespace ExportDocManager.Domain.Tests;

public sealed class CrossPlatformFileNamePolicyTests
{
    [Theory]
    [InlineData("invoice:2026?.pdf", "invoice_2026_.pdf")]
    [InlineData("folder/name\\part", "folder_name_part")]
    [InlineData("line\nbreak", "line_break")]
    public void ReplaceInvalidCharacters_ShouldUseStableCrossPlatformRules(
        string input,
        string expected)
    {
        Assert.True(CrossPlatformFileNamePolicy.ContainsInvalidCharacters(input));
        Assert.Equal(expected, CrossPlatformFileNamePolicy.ReplaceInvalidCharacters(input, '_'));
    }

    [Fact]
    public void ValidUnicodeFileName_ShouldRemainUnchanged()
    {
        const string value = "商业发票 2026.pdf";

        Assert.False(CrossPlatformFileNamePolicy.ContainsInvalidCharacters(value));
        Assert.Same(value, CrossPlatformFileNamePolicy.ReplaceInvalidCharacters(value, '_'));
    }
}

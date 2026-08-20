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

    [Theory]
    [InlineData("CON.xml")]
    [InlineData("PRN")]
    [InlineData("com9.sqlite")]
    [InlineData("LPT1.txt")]
    [InlineData("CON .archive.zip")]
    public void ReservedWindowsDeviceNames_ShouldBeRejectedAcrossPlatforms(string value)
    {
        Assert.True(CrossPlatformFileNamePolicy.IsReservedDeviceName(value));
        Assert.False(CrossPlatformFileNamePolicy.IsSafeFileName(value));
        Assert.StartsWith("_", CrossPlatformFileNamePolicy.SanitizeFileNamePart(value));
    }

    [Fact]
    public void Sanitization_ShouldNormalizeUnicodeAndTrimWindowsTrailingCharacters()
    {
        string decomposed = "e\u0301 report. ";

        Assert.Equal("é report", CrossPlatformFileNamePolicy.SanitizeFileNamePart(decomposed));
    }

    [Fact]
    public void Sanitization_ShouldRespectPortableUtf8ComponentBudgetAndPreserveExtension()
    {
        string value = new string('报', 200) + ".pdf";

        string sanitized = CrossPlatformFileNamePolicy.SanitizeFileNamePart(value);

        Assert.EndsWith(".pdf", sanitized, StringComparison.Ordinal);
        Assert.True(CrossPlatformFileNamePolicy.IsSafeFileName(sanitized));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(sanitized) <=
                    CrossPlatformFileNamePolicy.MaximumPortableComponentUtf8Bytes);
    }

    [Fact]
    public void Sanitization_ShouldPreserveExplicitEmptyFallbackContract()
    {
        Assert.Equal(string.Empty, CrossPlatformFileNamePolicy.SanitizeFileNamePart("...", '_', string.Empty));
        Assert.Equal("file", CrossPlatformFileNamePolicy.SanitizeFileNamePart("..."));
    }

}

using System.Text;
using ExportDocManager.Services.Data;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class TabularImportReaderTests
{
    [Fact]
    public async Task ReadCsv_ShouldHandleCrLfAndQuotedEscapes()
    {
        const string csv = "Name,Note\r\nAlice,\"He said \"\"hello\"\"\"\r\nBob,plain\r\n";

        var rows = await ReadAsync(csv, maximumRows: 10);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["Name", "Note"], rows[0]);
        Assert.Equal(["Alice", "He said \"hello\""], rows[1]);
        Assert.Equal(["Bob", "plain"], rows[2]);
    }

    [Fact]
    public async Task ReadCsv_ShouldPreserveQuotedLineBreaksAsNormalizedNewline()
    {
        const string csv = "Name,Address\r\nAlice,\"Line 1\r\nLine 2\"\r\n";

        var rows = await ReadAsync(csv, maximumRows: 10);

        Assert.Equal("Line 1\nLine 2", rows[1][1]);
    }

    [Theory]
    [InlineData("Name,Note\r\nAlice,\"unclosed\r\n")]
    [InlineData("Name,Note\r\nAlice,\"closed\"oops\r\n")]
    [InlineData("Name,Note\r\nAlice,plain\"quote\r\n")]
    public async Task ReadCsv_ShouldRejectMalformedQuotes(string csv)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(csv, maximumRows: 10));
    }

    [Fact]
    public async Task ReadCsv_ShouldRejectRowsBeyondDataLimit()
    {
        const string csv = "Name\r\nA\r\nB\r\n";

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(csv, maximumRows: 1));

        Assert.Contains("超过", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadCsv_ShouldRejectOversizedFieldInsteadOfGrowingWithoutBound()
    {
        string csv = "Name\r\n\"" + new string('x', 1_000_001) + "\"\r\n";

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(csv, maximumRows: 1));

        Assert.Contains("单个字段", error.Message, StringComparison.Ordinal);
    }

    private static Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(string csv, int maximumRows) =>
        TabularImportReader.ReadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)),
            "import.csv",
            maximumRows);
}

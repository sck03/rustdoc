using System.IO.Compression;
using System.Text;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;
using ExportDocManager.Utils;
using PdfSharp.Pdf;

namespace ExportDocManager.Infrastructure.Tests;

[Collection(LocalSecretProtectionCollection.Name)]
public sealed class SecurityAndResourceBoundaryTests
{
    [Fact]
    public void PathBoundaryComparer_ShouldMatchPlatformPathCaseSemantics()
    {
        Assert.Equal(
            OperatingSystem.IsWindows(),
            PathBoundaryHelper.PathComparer.Equals("managed/Root", "managed/root"));

        string volumeRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))
            ?? throw new InvalidOperationException("Temporary path does not have a volume root.");
        string candidate = Path.Combine(volumeRoot, $"edm-root-boundary-{Guid.NewGuid():N}");

        Assert.True(PathBoundaryHelper.IsWithinRoot(candidate, volumeRoot));
        Assert.Equal(
            Path.GetFullPath(candidate),
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                candidate,
                volumeRoot,
                "Root path boundary validation failed."));
    }

    [Fact]
    public void LocalSecretProtector_ShouldUseRandomNonceAndRejectTampering()
    {
        string root = CreateTempDirectory("secret-protector");
        try
        {
            var first = new LocalSecretProtector(root);
            var second = new LocalSecretProtector(root);
            string encryptedOne = first.Protect("数据库密码");
            string encryptedTwo = first.Protect("数据库密码");

            Assert.NotEqual(encryptedOne, encryptedTwo);
            Assert.Equal("数据库密码", second.Unprotect(encryptedOne));
            string tampered = encryptedOne[..^1] + (encryptedOne[^1] == 'A' ? 'B' : 'A');
            Assert.Throws<InvalidDataException>(() => second.Unprotect(tampered));
            Assert.False(second.TryUnprotect(tampered, out string plainText));
            Assert.Null(plainText);
            Assert.Throws<InvalidOperationException>(() => second.Protect(encryptedOne));
            Assert.Null(second.Unprotect("plain-text-secret"));
            Assert.False(second.TryUnprotect("plain-text-secret", out plainText));
            Assert.Null(plainText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BoundedStreamHelper_ShouldEnforceLimitForChunkedInput()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("123456"));
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PayloadLimitExceededException>(() =>
            BoundedStreamHelper.CopyToAsync(source, destination, maximumBytes: 5));
    }

    [Fact]
    public async Task BoundedStreamHelper_ShouldAllowAnEmptyStreamWithZeroBudget()
    {
        await using var source = new MemoryStream();
        await using var destination = new MemoryStream();

        Assert.Equal(0, await BoundedStreamHelper.CopyToAsync(source, destination, maximumBytes: 0));
    }

    [Fact]
    public async Task BoundedTextLineReader_ShouldPreserveBufferedLinesAndTrimCarriageReturn()
    {
        using var reader = new BoundedTextLineReader(new StringReader("first\r\nsecond\n"));

        Assert.Equal("first", await reader.ReadLineAsync(16));
        Assert.Equal("second", await reader.ReadLineAsync(16));
        Assert.Null(await reader.ReadLineAsync(16));
    }

    [Fact]
    public async Task BoundedTextLineReader_ShouldRejectOversizedSidecarResponse()
    {
        using var reader = new BoundedTextLineReader(new StringReader("123456\n"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadLineAsync(maximumCharacters: 5));

        Assert.Contains("5", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundedTextCollector_ShouldCapSidecarDiagnostics()
    {
        var collector = new BoundedTextCollector(maximumCharacters: 12);

        collector.AppendLine("first");
        collector.AppendLine("second-value");
        collector.AppendLine("ignored");

        string text = collector.GetText();
        Assert.True(text.Length <= 12);
        Assert.StartsWith("first | ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZipArchiveHelper_ShouldRejectTraversalEntries()
    {
        string root = CreateTempDirectory("zip-boundary");
        string packagePath = Path.Combine(root, "unsafe.zip");
        string targetPath = Path.Combine(root, "target");
        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                await using var writer = new StreamWriter(
                    archive.CreateEntry("../outside.txt").Open(),
                    Encoding.UTF8);
                await writer.WriteAsync("unsafe");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, targetPath));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ZipArchiveHelper_ShouldRejectPortablePathAmbiguity()
    {
        string root = CreateTempDirectory("zip-portable-path");
        string packagePath = Path.Combine(root, "unsafe.zip");
        string targetPath = Path.Combine(root, "target");
        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                await using var writer = new StreamWriter(
                    archive.CreateEntry("report.txt:secret").Open(),
                    Encoding.UTF8);
                await writer.WriteAsync("unsafe");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, targetPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ExcelWorkbookResourcePolicy_ShouldRestoreStreamPositionForValidPackage()
    {
        using var package = CreateZipStream(("[Content_Types].xml", "types"), ("xl/workbook.xml", "workbook"));
        package.Position = 2;

        ExcelWorkbookResourcePolicy.ValidateOpenXmlPackage(package);

        Assert.Equal(2, package.Position);
    }

    [Fact]
    public void ExcelWorkbookResourcePolicy_ShouldRejectExpandedEntryBeyondBudget()
    {
        using var package = CreateZipStream(("xl/worksheets/sheet1.xml", "123456789"));
        var limits = new ExcelWorkbookResourceLimits(
            MaximumPackageBytes: 1024,
            MaximumEntries: 4,
            MaximumEntryBytes: 8,
            MaximumTotalExpandedBytes: 16,
            MaximumCompressionRatio: 500);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ExcelWorkbookResourcePolicy.ValidateOpenXmlPackage(package, limits));

        Assert.Contains("条目展开后", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextLogCleanup_ShouldApplyRetentionCountAcrossLogAndTxtFiles()
    {
        string root = CreateTempDirectory("text-log-retention");
        try
        {
            string oldest = Path.Combine(root, "oldest.log");
            string middle = Path.Combine(root, "middle.txt");
            string newest = Path.Combine(root, "newest.log");
            File.WriteAllText(oldest, "oldest");
            File.WriteAllText(middle, "middle");
            File.WriteAllText(newest, "newest");
            File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-3));
            File.SetLastWriteTimeUtc(middle, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddMinutes(-1));

            TextLogCleanupSummary summary = TextLogCleanupHelper.Clean(
                root,
                retentionDays: 0,
                retainedFileCount: 2);

            Assert.Equal(1, summary.DeletedByCount);
            Assert.False(File.Exists(oldest));
            Assert.True(File.Exists(middle));
            Assert.True(File.Exists(newest));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void TextLogCleanup_ShouldKeepOnlyTheConfiguredTailOfOversizedFile()
    {
        string root = CreateTempDirectory("text-log-trim");
        try
        {
            string logPath = Path.Combine(root, "oversized.log");
            byte[] content = new byte[(1024 * 1024) + (64 * 1024)];
            content.AsSpan(0, 64 * 1024).Fill(0x11);
            content.AsSpan(64 * 1024).Fill(0x7a);
            File.WriteAllBytes(logPath, content);

            TextLogCleanupHelper.Clean(
                root,
                retentionDays: 0,
                retainedFileCount: 0,
                maxFileSizeMB: 1);

            byte[] trimmed = File.ReadAllBytes(logPath);
            Assert.Equal(1024 * 1024, trimmed.Length);
            Assert.True(trimmed.All(value => value == 0x7a));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void TextLogCleanup_ShouldTrimUppercaseLogExtensionsAcrossPlatforms()
    {
        string root = CreateTempDirectory("text-log-uppercase-trim");
        try
        {
            string logPath = Path.Combine(root, "oversized.LOG");
            using (var stream = File.Create(logPath))
            {
                stream.SetLength((1024 * 1024) + 1);
            }

            TextLogCleanupHelper.Clean(
                root,
                retentionDays: 0,
                retainedFileCount: 0,
                maxFileSizeMB: 1);

            Assert.Equal(1024 * 1024, new FileInfo(logPath).Length);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void PdfMergeService_ShouldRejectSinglePdfBeyondPageLimit()
    {
        string root = CreateTempDirectory("pdf-page-limit");
        string sourcePath = Path.Combine(root, "oversized.pdf");
        string destinationPath = Path.Combine(root, "merged.pdf");
        try
        {
            using (var document = new PdfDocument())
            {
                for (int page = 0; page < 1001; page++)
                {
                    document.AddPage();
                }
                document.Save(sourcePath);
            }

            var service = new PdfMergeService();
            var exception = Assert.Throws<InvalidDataException>(() =>
                service.Merge([sourcePath], destinationPath));

            Assert.Contains("1000", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"edm-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static MemoryStream CreateZipStream(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(entry.Name, CompressionLevel.Optimal).Open(),
                    new UTF8Encoding(false));
                writer.Write(entry.Content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

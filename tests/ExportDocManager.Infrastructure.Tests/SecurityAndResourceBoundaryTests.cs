using System.IO.Compression;
using System.Text;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Tools;
using ExportDocManager.Utils;
using PdfSharp.Drawing;
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
    public void PortablePathKey_ShouldNormalizeUnicodeAndSeparators()
    {
        Assert.True(PortablePathKey.Comparer.Equals(
            "Reports\\e\u0301.HTML",
            "reports/é.html"));
        Assert.Equal("Reports/é.html", PortablePathKey.NormalizeRelativePath("Reports\\e\u0301.html"));
    }

    [Theory]
    [InlineData("/absolute/file.txt")]
    [InlineData("\\absolute\\file.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    public void PortablePathKey_ShouldRejectHostIndependentAbsolutePaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => PortablePathKey.NormalizeRelativePath(path));
    }

    [Fact]
    public void ControlledFileSystemEnumerator_ShouldEnumerateOnlySafeFiles()
    {
        string root = CreateTempDirectory("controlled-enumeration");
        try
        {
            string nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            File.WriteAllText(Path.Combine(nested, "a.txt"), "a");

            IReadOnlyList<string> files = ControlledFileSystemEnumerator.EnumerateFiles(root);

            Assert.Equal(2, files.Count);
            Assert.Equal(
                [Path.Combine(root, "b.txt"), Path.Combine(nested, "a.txt")],
                files);
            Assert.Single(ControlledFileSystemEnumerator.EnumerateImmediateFiles(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ControlledFileSystemEnumerator_ShouldRejectDirectorySymbolicLink()
    {
        string root = CreateTempDirectory("controlled-enumeration-link");
        string outside = CreateTempDirectory("controlled-enumeration-outside");
        try
        {
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
            string link = Path.Combine(root, "linked");
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                // Symbolic-link creation requires an elevated/dev-mode capability on
                // some Windows runners.  The same assertion runs on capable hosts.
                return;
            }

            Assert.Throws<InvalidDataException>(() => ControlledFileSystemEnumerator.EnumerateFiles(root));
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(outside);
        }
    }

    [Fact]
    public void SingleWindowSubmitPackagePath_ShouldUseManagedFilesOnly()
    {
        string root = CreateTempDirectory("single-window-package-path");
        string managed = Path.Combine(root, "Inbox", "batch.swpkg");
        string outside = Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Guid.NewGuid():N}.swpkg");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
            File.WriteAllText(managed, "package");
            File.WriteAllText(outside, "outside");

            Assert.Equal(
                Path.GetFullPath(managed),
                ManualImportClientBridge.TryResolveManagedSubmitPackagePath(managed, root));
            Assert.Equal(
                Path.GetFullPath(managed),
                ManualImportClientBridge.TryResolveManagedSubmitPackagePath(Path.Combine("Inbox", "batch.swpkg"), root));
            Assert.Null(ManualImportClientBridge.TryResolveManagedSubmitPackagePath(outside, root));
            Assert.Null(ManualImportClientBridge.TryResolveManagedSubmitPackagePath(Path.Combine(root, "..", Path.GetFileName(outside)), root));
            Assert.Null(ManualImportClientBridge.TryResolveManagedSubmitPackagePath(Path.Combine(root, "Inbox"), root));

            string link = Path.Combine(root, "Inbox", "linked.swpkg");
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            Assert.Null(ManualImportClientBridge.TryResolveManagedSubmitPackagePath(link, root));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AtomicFileHelper_ShouldNeverRecursivelyDeleteThroughDirectoryLink()
    {
        string root = CreateTempDirectory("atomic-cleanup-link");
        string outside = CreateTempDirectory("atomic-cleanup-outside");
        string outsideFile = Path.Combine(outside, "keep.txt");
        string link = Path.Combine(root, "linked");
        try
        {
            File.WriteAllText(outsideFile, "must remain");
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            AtomicFileHelper.TryDeleteDirectory(root);
            Assert.True(File.Exists(outsideFile));
            Assert.True(Directory.Exists(root));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AtomicFileHelper.TryDeleteDirectoryAsync(root));
            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link, recursive: false);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
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
            Assert.False(second.TryUnprotect(tampered, out string? plainText));
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
    public void EmailAttachmentPolicy_ShouldRejectExcessiveAttachmentCount()
    {
        string root = CreateTempDirectory("email-attachment-limit");
        try
        {
            var paths = Enumerable
                .Range(1, EmailAttachmentPolicy.MaximumAttachmentCount + 1)
                .Select(index =>
                {
                    string path = Path.Combine(root, $"attachment-{index}.txt");
                    File.WriteAllText(path, "test");
                    return path;
                })
                .ToList();

            Assert.Throws<ExportDocManager.Services.Errors.ServiceValidationException>(() =>
                EmailAttachmentPolicy.ValidateAndNormalize(paths));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void EmailAttachmentPolicy_ShouldRejectUnsupportedAndSpoofedTypes()
    {
        string root = CreateTempDirectory("email-attachment-types");
        try
        {
            string executable = Path.Combine(root, "payload.exe");
            string spoofedPdf = Path.Combine(root, "payload.pdf");
            string validPdf = Path.Combine(root, "document.pdf");
            File.WriteAllBytes(executable, [0x4D, 0x5A]);
            File.WriteAllText(spoofedPdf, "not a pdf", Encoding.UTF8);
            File.WriteAllBytes(validPdf, "%PDF-1.7\n"u8.ToArray());

            Assert.Throws<ServiceValidationException>(() =>
                EmailAttachmentPolicy.ValidateAndNormalize([executable]));
            Assert.Throws<ServiceValidationException>(() =>
                EmailAttachmentPolicy.ValidateAndNormalize([spoofedPdf]));
            Assert.Equal(
                Path.GetFullPath(validPdf),
                Assert.Single(EmailAttachmentPolicy.ValidateAndNormalize([validPdf])));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void EmailRecipientPolicy_ShouldApplyAddressAndDomainRulesFailClosed()
    {
        var config = new EmailConfig
        {
            RecipientAllowList = "@example.com\nrecipient@partner.test",
            RecipientBlockList = "blocked@example.com\n@blocked.example.com"
        };

        Assert.Equal(
            "sales@sub.example.com",
            EmailRecipientPolicy.ValidateAndNormalize("Sales <sales@sub.example.com>", config));
        Assert.Equal(
            "recipient@partner.test",
            EmailRecipientPolicy.ValidateAndNormalize("recipient@partner.test", config));
        Assert.Throws<PermissionDeniedException>(() =>
            EmailRecipientPolicy.ValidateAndNormalize("blocked@example.com", config));
        Assert.Throws<PermissionDeniedException>(() =>
            EmailRecipientPolicy.ValidateAndNormalize("user@blocked.example.com", config));
        Assert.Throws<PermissionDeniedException>(() =>
            EmailRecipientPolicy.ValidateAndNormalize("user@outside.test", config));
        Assert.Throws<ServiceValidationException>(() =>
            EmailRecipientPolicy.NormalizeRules("@bad/domain", "收件人白名单"));
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
    public async Task ZipArchiveHelper_ShouldRejectUnicodeNormalizationCollisions()
    {
        string root = CreateTempDirectory("zip-unicode-collision");
        string packagePath = Path.Combine(root, "unsafe.zip");
        string targetPath = Path.Combine(root, "target");
        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("é.txt");
                archive.CreateEntry("e\u0301.txt");
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
    public void ExcelWorkbookResourcePolicy_ShouldRejectWorksheetBeyondLogicalBudget()
    {
        using var package = CreateZipStream((
            "xl/worksheets/sheet1.xml",
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"11\"><c r=\"A11\" /></row></sheetData></worksheet>"));
        var limits = new ExcelWorkbookResourceLimits(
            MaximumPackageBytes: 4096,
            MaximumEntries: 4,
            MaximumEntryBytes: 2048,
            MaximumTotalExpandedBytes: 4096,
            MaximumCompressionRatio: 500,
            MaximumWorksheets: 1,
            MaximumRowsPerWorksheet: 10,
            MaximumColumnsPerWorksheet: 4,
            MaximumTotalCells: 10);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ExcelWorkbookResourcePolicy.ValidateOpenXmlPackage(package, limits));

        Assert.Contains("行数超过", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcelWorkbookResourcePolicy_ShouldRejectOverflowingCellRowReference()
    {
        using var package = CreateZipStream((
            "xl/worksheets/sheet1.xml",
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row><c r=\"A9999999999\" /></row></sheetData></worksheet>"));

        var exception = Assert.Throws<InvalidDataException>(() =>
            ExcelWorkbookResourcePolicy.ValidateOpenXmlPackage(package));

        Assert.Contains("单元格行号无效", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcelWorkbookResourceBudget_ShouldRejectCellCountBeforeAllocatingRows()
    {
        var limits = new ExcelWorkbookResourceLimits(
            MaximumWorksheets: 1,
            MaximumRowsPerWorksheet: 10,
            MaximumColumnsPerWorksheet: 10,
            MaximumTotalCells: 4);
        var budget = new ExcelWorkbookResourceBudget(limits);
        ExcelWorksheetResourceBudget worksheet = budget.StartWorksheet("LegacySheet");

        var exception = Assert.Throws<InvalidDataException>(() =>
            worksheet.RegisterRow(rowNumber: 1, maximumColumn: 5, cellCount: 5));

        Assert.Contains("单元格总数", exception.Message, StringComparison.Ordinal);
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
    public void TextLogCleanup_ShouldRejectLinkLikeChildrenBeforeDeletingAnything()
    {
        string root = CreateTempDirectory("text-log-link");
        string outside = CreateTempDirectory("text-log-link-outside");
        string link = Path.Combine(root, "linked");
        try
        {
            string outsideLog = Path.Combine(outside, "outside.log");
            File.WriteAllText(outsideLog, "must remain");
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            Assert.Throws<InvalidDataException>(() =>
                TextLogCleanupHelper.Clean(root, retentionDays: 1, retainedFileCount: 1));
            Assert.True(File.Exists(outsideLog));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link, recursive: false);
            }

            DeleteDirectory(root);
            DeleteDirectory(outside);
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
                for (int page = 0; page < PdfMergeService.MaxPagesPerFile + 1; page++)
                {
                    document.AddPage();
                }
                document.Save(sourcePath);
            }

            var service = new PdfMergeService();
            var exception = Assert.Throws<InvalidDataException>(() =>
                service.Merge([sourcePath], destinationPath));

            Assert.Contains(PdfMergeService.MaxPagesPerFile.ToString(), exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void PdfMergeService_ShouldRejectOversizedPageDimensions()
    {
        string root = CreateTempDirectory("pdf-page-dimensions");
        string sourcePath = Path.Combine(root, "oversized-page.pdf");
        string destinationPath = Path.Combine(root, "merged.pdf");
        try
        {
            using (var document = new PdfDocument())
            {
                PdfPage page = document.AddPage();
                page.Width = XUnit.FromPoint(PdfMergeService.MaxPageDimensionPoints + 1);
                page.Height = XUnit.FromPoint(100);
                document.Save(sourcePath);
            }

            var error = Assert.Throws<InvalidDataException>(() =>
                new PdfMergeService().Merge([sourcePath], destinationPath));

            Assert.Contains("页面尺寸", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void PdfMergeService_ShouldRejectEstimatedWorkingSetBeyondBudget()
    {
        long inputBytes = PdfMergeService.MaxEstimatedWorkingSetBytes / 3;

        var error = Assert.Throws<InvalidDataException>(() =>
            PdfMergeService.EnsureWithinWorkingSetBudget(
                inputBytes,
                totalPages: 200,
                totalPageArea: 200 * 595d * 842d));

        Assert.Contains("512 MB", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfMergeService_WorkingSetEstimate_ShouldAccountForContentAndPages()
    {
        long small = PdfMergeService.EstimateWorkingSetBytes(
            totalInputBytes: 4L * 1024L * 1024L,
            totalPages: 10,
            totalPageArea: 10 * 595d * 842d);
        long complex = PdfMergeService.EstimateWorkingSetBytes(
            totalInputBytes: 32L * 1024L * 1024L,
            totalPages: 100,
            totalPageArea: 100 * 595d * 842d);

        Assert.True(small > 0);
        Assert.True(complex > small);
        Assert.True(complex < PdfMergeService.MaxEstimatedWorkingSetBytes);
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

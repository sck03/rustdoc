using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class FileCopyHelperTests
{
    [Fact]
    public async Task CopyAsync_WithNoOverwrite_DoesNotReplaceAnExistingDestination()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "edm-file-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.xml");
        string target = Path.Combine(root, "target.xml");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(target, "original");

        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                FileCopyHelper.CopyAsync(source, target, overwrite: false));

            Assert.Equal("original", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.EnumerateFiles(root, ".target.*.tmp.xml"));
        }
        finally
        {
            AtomicFileHelper.TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_WithOverwrite_ReplacesDestinationAtomically()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "edm-file-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.xml");
        string target = Path.Combine(root, "target.xml");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(target, "original");

        try
        {
            await FileCopyHelper.CopyAsync(source, target, overwrite: true);
            Assert.Equal("source", await File.ReadAllTextAsync(target));
            Assert.Empty(Directory.EnumerateFiles(root, ".target.*.tmp.xml"));
        }
        finally
        {
            AtomicFileHelper.TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CopyAsync_WhenSourceOpenFails_CleansReservedTemporaryFile()
    {
        // File-share enforcement is deterministic on Windows, while Unix file
        // sharing is advisory.  The cross-platform copy behavior is covered by
        // the other tests; this case specifically guards the Windows failure
        // path where the sibling temp name has already been reserved.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "edm-file-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.xml");
        string target = Path.Combine(root, "target.xml");
        await File.WriteAllTextAsync(source, "source");

        await using var sourceLock = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        try
        {
            await Assert.ThrowsAnyAsync<IOException>(() =>
                FileCopyHelper.CopyAsync(source, target, overwrite: false));

            Assert.False(File.Exists(target));
            Assert.Empty(Directory.EnumerateFiles(root, ".target.*.tmp.xml"));
        }
        finally
        {
            await sourceLock.DisposeAsync();
            AtomicFileHelper.TryDeleteDirectory(root);
        }
    }
}

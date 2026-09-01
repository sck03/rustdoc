using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class SingleWindowClientDispatchAtomicityTests
{
    [Fact]
    public async Task PublishPayloadFiles_ShouldRetainCommittedFilesAndRemoveOnlyPendingFilesWhenCommitFails()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "edm-single-window-dispatch-" + Guid.NewGuid().ToString("N"));
        string stagingRoot = Path.Combine(root, "staging");
        string outBoxRoot = Path.Combine(root, "outbox");
        Directory.CreateDirectory(stagingRoot);
        string first = Path.Combine(stagingRoot, "first.xml");
        string second = Path.Combine(stagingRoot, "second.xml");
        await File.WriteAllTextAsync(first, "<first />");
        await File.WriteAllTextAsync(second, "<second />");

        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                ManualImportClientBridge.PublishPayloadFilesAsync(
                    [first, second],
                    outBoxRoot,
                    "COO-V001-TEST",
                    CancellationToken.None,
                    (index, _) =>
                    {
                        if (index == 1)
                        {
                            // Simulate the narrow post-commit window: an observer
                            // moves the pending file into the official OutBox and
                            // then interrupts the caller before it records the
                            // path in its in-memory list.
                            string pending = Assert.Single(
                                Directory.EnumerateFiles(outBoxRoot, "*.pending-*"));
                            File.Move(pending, Path.Combine(outBoxRoot, "second.xml"));
                            throw new InvalidOperationException("deterministic commit failure");
                        }
                    }));

            Assert.Contains("部分发布", failure.Message, StringComparison.Ordinal);
            Assert.Contains(Path.Combine(outBoxRoot, "first.xml"), Directory.EnumerateFiles(outBoxRoot, "*", SearchOption.AllDirectories));
            Assert.Contains(Path.Combine(outBoxRoot, "second.xml"), Directory.EnumerateFiles(outBoxRoot, "*", SearchOption.AllDirectories));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(outBoxRoot, "*", SearchOption.AllDirectories),
                path => path.Contains(".pending-", StringComparison.OrdinalIgnoreCase));

            var published = await ManualImportClientBridge.PublishPayloadFilesAsync(
                [first, second],
                outBoxRoot,
                "COO-V001-TEST",
                CancellationToken.None);

            Assert.Equal(2, published.Count);
            Assert.All(published, path => Assert.True(File.Exists(path)));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(outBoxRoot, "*", SearchOption.AllDirectories),
                path => path.Contains(".pending-", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PublishPayloadFiles_ShouldSanitizeCollisionNamesAndRejectNonXmlPayloads()
    {
        string root = Path.Combine(Path.GetTempPath(), "edm-single-window-name-" + Guid.NewGuid().ToString("N"));
        string stagingRoot = Path.Combine(root, "staging");
        string outBoxRoot = Path.Combine(root, "outbox");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(outBoxRoot);
        string source = Path.Combine(stagingRoot, "payload.xml");
        await File.WriteAllTextAsync(source, "<payload />");
        await File.WriteAllTextAsync(Path.Combine(outBoxRoot, "payload.xml"), "<existing />");

        try
        {
            var published = await ManualImportClientBridge.PublishPayloadFilesAsync(
                [source], outBoxRoot, "../evil\\batch:01", CancellationToken.None);
            string publishedPath = Assert.Single(published);
            Assert.True(PathBoundaryHelper.IsWithinRoot(publishedPath, outBoxRoot));
            Assert.True(CrossPlatformFileNamePolicy.IsSafeFileName(Path.GetFileName(publishedPath)));
            Assert.EndsWith(".xml", publishedPath, StringComparison.OrdinalIgnoreCase);

            string nonXml = Path.Combine(stagingRoot, "payload.txt");
            await File.WriteAllTextAsync(nonXml, "not xml");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ManualImportClientBridge.PublishPayloadFilesAsync(
                    [nonXml], outBoxRoot, "batch", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

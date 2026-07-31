using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class SingleWindowClientDispatchAtomicityTests
{
    [Fact]
    public async Task PublishPayloadFiles_ShouldRemoveEveryVisibleAndPendingFileWhenCommitFails()
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
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ManualImportClientBridge.PublishPayloadFilesAsync(
                    [first, second],
                    outBoxRoot,
                    "COO-V001-TEST",
                    CancellationToken.None,
                    (index, _) =>
                    {
                        if (index == 1)
                        {
                            throw new InvalidOperationException("deterministic commit failure");
                        }
                    }));

            Assert.Empty(Directory.EnumerateFiles(outBoxRoot, "*", SearchOption.AllDirectories));

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
}

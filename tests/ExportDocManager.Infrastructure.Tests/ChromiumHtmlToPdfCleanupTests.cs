using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ChromiumHtmlToPdfCleanupTests
{
    [Fact]
    public void Constructor_ShouldRemoveCrashLeftoversButPreserveUnmanagedCacheDirectories()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "ExportDocManagerTests",
            nameof(ChromiumHtmlToPdfCleanupTests),
            Guid.NewGuid().ToString("N"));
        try
        {
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(appRoot);
            Directory.CreateDirectory(dataRoot);
            var paths = new RuntimeAppPathProvider(appRoot, dataRoot);
            string reportRoot = Path.Combine(paths.CacheRoot, "ReportPdf");
            string abandoned = Path.Combine(reportRoot, "r-0123456789abcdef");
            string unmanaged = Path.Combine(reportRoot, "keep-me");
            Directory.CreateDirectory(abandoned);
            Directory.CreateDirectory(unmanaged);
            File.WriteAllText(Path.Combine(abandoned, "output.pdf"), "partial");

            _ = new ChromiumHtmlToPdfService(paths);

            Assert.False(Directory.Exists(abandoned));
            Assert.True(Directory.Exists(unmanaged));
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

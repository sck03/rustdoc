using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Infrastructure.Tests
{
    public class ReportTemplateStorageDiagnosticsServiceTests
    {
        [Fact]
        public async Task CheckAsync_WhenTemplateRootWritable_ShouldRemoveProbeFile()
        {
            string root = CreateTestRoot("writable");
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(appRoot);

            try
            {
                var service = new ReportTemplateStorageDiagnosticsService(
                    new RuntimeAppPathProvider(appRoot, dataRoot));

                var result = await service.CheckAsync();

                Assert.True(result.Exists);
                Assert.True(result.Writable);
                Assert.Equal(Path.Combine(appRoot, "Templates"), result.TemplateRoot);
                Assert.Contains("Templates", result.StoragePolicy, StringComparison.Ordinal);
                Assert.Empty(Directory.GetFiles(result.TemplateRoot, ".edm-template-write-check-*.tmp"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Fact]
        public async Task CheckAsync_WhenTemplateRootIsAFile_ShouldReturnReadableFailure()
        {
            string root = CreateTestRoot("blocked");
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(appRoot);
            File.WriteAllText(Path.Combine(appRoot, "Templates"), "blocked");

            try
            {
                var service = new ReportTemplateStorageDiagnosticsService(
                    new RuntimeAppPathProvider(appRoot, dataRoot));

                var result = await service.CheckAsync();

                Assert.False(result.Exists);
                Assert.False(result.Writable);
                Assert.Contains("模板目录不可写", result.Message, StringComparison.Ordinal);
                Assert.Contains("非系统盘运行目录", result.Message, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static string CreateTestRoot(string prefix)
        {
            string path = Path.Combine(
                FindRepositoryRoot(),
                ".codex-runtime",
                "template-storage-diagnostics-tests",
                $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string FindRepositoryRoot()
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (File.Exists(Path.Combine(directory, "ExportDocManager.sln")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate ExportDocManager.sln from test output.");
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}

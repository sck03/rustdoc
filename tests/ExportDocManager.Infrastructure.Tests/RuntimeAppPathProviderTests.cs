using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests
{
    public class RuntimeAppPathProviderTests
    {
        [Fact]
        public void Constructor_WhenDataRootMissing_ShouldUseAppRootApp_Data()
        {
            string appRoot = CreateTempDirectory();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot);

                Assert.Equal(NormalizeRoot(appRoot), provider.AppRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(appRoot), "App_Data"), provider.DataRoot);
                Assert.Equal(Path.Combine(provider.DataRoot, "Database"), provider.DatabaseRoot);
                Assert.Equal(Path.Combine(provider.DataRoot, "Backups"), provider.BackupRoot);
                Assert.Equal(Path.Combine(provider.AppRoot, "Templates"), provider.TemplateRoot);
                Assert.Equal(Path.Combine(provider.AppRoot, "Resources"), provider.ResourceRoot);
                Assert.Equal(Path.Combine(provider.AppRoot, "Browsers"), provider.BrowserRoot);
                Assert.Equal(Path.Combine(provider.AppRoot, "Tools"), provider.ToolRoot);
                Assert.Equal(Path.Combine(provider.AppRoot, "OcrModels"), provider.OcrModelRoot);
                Assert.Equal(Path.Combine(provider.DataRoot, "Logs"), provider.LogRoot);
                Assert.Equal(Path.Combine(provider.DataRoot, "WebView"), provider.WebViewRoot);
                Assert.True(Directory.Exists(provider.DatabaseRoot));
                Assert.True(Directory.Exists(provider.LogRoot));
                Assert.True(Directory.Exists(provider.WebViewRoot));
            }
            finally
            {
                DeleteDirectory(appRoot);
            }
        }

        [Fact]
        public void Constructor_WhenDataRootProvided_ShouldUseConfiguredBusinessDataRoot()
        {
            string appRoot = CreateTempDirectory();
            string dataRoot = CreateTempDirectory();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot, dataRoot);

                Assert.Equal(NormalizeRoot(appRoot), provider.AppRoot);
                Assert.Equal(NormalizeRoot(dataRoot), provider.DataRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(appRoot), "Templates"), provider.TemplateRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(appRoot), "Resources"), provider.ResourceRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(appRoot), "Browsers"), provider.BrowserRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(appRoot), "Tools"), provider.ToolRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(appRoot), "OcrModels"), provider.OcrModelRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(dataRoot), "Logs"), provider.LogRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(dataRoot), "Backups"), provider.BackupRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(dataRoot), "SingleWindow"), provider.SingleWindowRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(dataRoot), "Security"), provider.SecurityRoot);
                Assert.Equal(Path.Combine(NormalizeRoot(dataRoot), "WebView"), provider.WebViewRoot);
                Assert.True(Directory.Exists(provider.SingleWindowRoot));
                Assert.True(Directory.Exists(provider.SecurityRoot));
                Assert.True(Directory.Exists(provider.WebViewRoot));
            }
            finally
            {
                DeleteDirectory(appRoot);
                DeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public void StableResourceProperties_ShouldNotCreateProgramRootDirectories()
        {
            string appRoot = CreateTempDirectory();
            string dataRoot = CreateTempDirectory();

            try
            {
                var provider = new RuntimeAppPathProvider(appRoot, dataRoot);
                string[] stableResourcePaths =
                [
                    provider.TemplateRoot,
                    provider.ResourceRoot,
                    provider.BrowserRoot,
                    provider.ToolRoot,
                    provider.OcrModelRoot
                ];

                Assert.All(stableResourcePaths, path => Assert.False(Directory.Exists(path)));
                Assert.True(Directory.Exists(provider.DatabaseRoot));
                Assert.All(stableResourcePaths, path => Assert.False(Directory.Exists(path)));
            }
            finally
            {
                DeleteDirectory(appRoot);
                DeleteDirectory(dataRoot);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string NormalizeRoot(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}

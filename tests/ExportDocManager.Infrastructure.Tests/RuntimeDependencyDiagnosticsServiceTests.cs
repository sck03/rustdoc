using ExportDocManager.Services;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class RuntimeDependencyDiagnosticsServiceTests
    {
        [Fact]
        public void Inspect_ShouldReuseRuntimeResolversWithoutCreatingStableResourceDirectories()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "runtime-dependency-tests", Guid.NewGuid().ToString("N"));
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            string browserPath = Path.Combine(appRoot, "Browsers", "chrome-headless-shell.exe");
            string modelRoot = Path.Combine(appRoot, "OcrModels", "PaddleOCR", "V6");
            string postgreSqlBin = Path.Combine(appRoot, "Tools", "PostgreSQL", "bin");
            string previousBrowser = Environment.GetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable);
            string previousOcr = Environment.GetEnvironmentVariable(OcrRuntimeAvailabilityInspector.RuntimeEnvironmentVariable);
            string previousPostgreSql = Environment.GetEnvironmentVariable(PostgreSqlToolLocator.BinRootEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(OcrRuntimeAvailabilityInspector.RuntimeEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(PostgreSqlToolLocator.BinRootEnvironmentVariable, null);

                Directory.CreateDirectory(Path.GetDirectoryName(browserPath)!);
                File.WriteAllText(browserPath, string.Empty);
                WriteOcrModelBundle(modelRoot);
                Directory.CreateDirectory(postgreSqlBin);
                File.WriteAllText(Path.Combine(postgreSqlBin, OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump"), string.Empty);

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var diagnostics = new RuntimeDependencyDiagnosticsService(pathProvider).Inspect();

                var renderer = Assert.Single(diagnostics, item => item.Key == "report-renderer");
                Assert.True(renderer.Ready);
                Assert.Equal("ready", renderer.Status);
                Assert.Equal(Path.GetFullPath(browserPath), renderer.ResolvedPath);

                var ocr = Assert.Single(diagnostics, item => item.Key == "ocr-runtime");
                Assert.Equal(OperatingSystem.IsWindows(), ocr.Ready);
                Assert.Equal(OperatingSystem.IsWindows() ? "ready" : "unsupported", ocr.Status);

                var postgreSql = Assert.Single(diagnostics, item => item.Key == "postgresql-tools");
                Assert.False(postgreSql.Ready);
                Assert.Equal("incomplete", postgreSql.Status);
                Assert.Contains("1/3", postgreSql.Message, StringComparison.Ordinal);

                string missingAppRoot = Path.Combine(root, "missing-app");
                var missingPaths = new RuntimeAppPathProvider(missingAppRoot, Path.Combine(root, "missing-data"));
                var missingDiagnostics = new RuntimeDependencyDiagnosticsService(missingPaths).Inspect();
                Assert.Contains(missingDiagnostics, item => item.Key == "report-renderer" && item.Status == "missing");
                Assert.Contains(missingDiagnostics, item => item.Key == "postgresql-tools" && item.Status == "missing");
                Assert.False(Directory.Exists(missingPaths.BrowserRoot));
                Assert.False(Directory.Exists(missingPaths.OcrModelRoot));
                Assert.False(Directory.Exists(missingPaths.ToolRoot));
            }
            finally
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, previousBrowser);
                Environment.SetEnvironmentVariable(OcrRuntimeAvailabilityInspector.RuntimeEnvironmentVariable, previousOcr);
                Environment.SetEnvironmentVariable(PostgreSqlToolLocator.BinRootEnvironmentVariable, previousPostgreSql);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static void WriteOcrModelBundle(string modelRoot)
        {
            string detRoot = Path.Combine(modelRoot, "det");
            string recRoot = Path.Combine(modelRoot, "rec");
            Directory.CreateDirectory(detRoot);
            Directory.CreateDirectory(recRoot);
            File.WriteAllText(Path.Combine(detRoot, "inference.onnx"), "model");
            File.WriteAllText(Path.Combine(detRoot, "inference.yml"), "detector: true");
            File.WriteAllText(Path.Combine(recRoot, "inference.onnx"), "model");
            File.WriteAllText(Path.Combine(recRoot, "inference.yml"), "character_dict:\n  - A\n  - B\n");
        }
    }
}

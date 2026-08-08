using ExportDocManager.Services;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.Tests
{
    [Collection(BrowserIntegrationCollection.Name)]
    public sealed class RuntimeDependencyDiagnosticsServiceTests
    {
        [Fact]
        public void Inspect_ShouldReuseRuntimeResolversWithoutCreatingStableResourceDirectories()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "runtime-dependency-tests", Guid.NewGuid().ToString("N"));
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            string browserPath = Path.Combine(
                appRoot,
                "Browsers",
                OperatingSystem.IsWindows() ? "chrome-headless-shell.exe" : "chrome-headless-shell");
            string modelRoot = Path.Combine(appRoot, "OcrModels", "PaddleOCR", "V6");
            string postgreSqlBin = Path.Combine(appRoot, "Tools", "PostgreSQL", "bin");
            string previousBrowser = Environment.GetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable);
            string previousCdpEndpoint = Environment.GetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable);
            string previousOcr = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");
            string previousPostgreSql = Environment.GetEnvironmentVariable(PostgreSqlToolLocator.BinRootEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, null);
                Environment.SetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable, null);
                Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", null);
                Environment.SetEnvironmentVariable(PostgreSqlToolLocator.BinRootEnvironmentVariable, null);

                WriteHeadlessShellBundle(browserPath);
                WriteOcrModelBundle(modelRoot);
                string ocrSidecar = Path.Combine(appRoot, "sidecar", "ocr", OperatingSystem.IsWindows() ? "exportdoc-ocr.exe" : "exportdoc-ocr");
                Directory.CreateDirectory(Path.GetDirectoryName(ocrSidecar)!);
                File.WriteAllText(ocrSidecar, string.Empty);
                Directory.CreateDirectory(postgreSqlBin);
                File.WriteAllText(Path.Combine(postgreSqlBin, OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump"), string.Empty);

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var browserResolver = new BrowserExecutableResolver(pathProvider, _ => { });
                var diagnostics = new RuntimeDependencyDiagnosticsService(
                    pathProvider,
                    browserResolver).Inspect();

                var renderer = Assert.Single(diagnostics, item => item.Key == "report-renderer");
                Assert.True(renderer.Ready);
                Assert.Equal("ready", renderer.Status);
                Assert.Equal(Path.GetFullPath(browserPath), renderer.ResolvedPath);
                var automation = Assert.Single(diagnostics, item => item.Key == "browser-automation");
                Assert.True(automation.Ready);
                Assert.Equal(Path.GetFullPath(browserPath), automation.ResolvedPath);

                var ocr = Assert.Single(diagnostics, item => item.Key == "ocr-runtime");
                Assert.True(ocr.Ready);
                Assert.Equal("ready", ocr.Status);

                var postgreSql = Assert.Single(diagnostics, item => item.Key == "postgresql-tools");
                Assert.False(postgreSql.Ready);
                Assert.Equal("incomplete", postgreSql.Status);
                Assert.Contains("1/3", postgreSql.Message, StringComparison.Ordinal);

                string missingAppRoot = Path.Combine(root, "missing-app");
                var missingPaths = new RuntimeAppPathProvider(missingAppRoot, Path.Combine(root, "missing-data"));
                var missingDiagnostics = new RuntimeDependencyDiagnosticsService(missingPaths).Inspect();
                Assert.Contains(missingDiagnostics, item => item.Key == "report-renderer" && item.Status == "missing");
                Assert.Contains(missingDiagnostics, item => item.Key == "browser-automation" && item.Status == "missing");
                Assert.Contains(missingDiagnostics, item => item.Key == "postgresql-tools" && item.Status == "missing");
                Assert.False(Directory.Exists(missingPaths.BrowserRoot));
                Assert.False(Directory.Exists(missingPaths.OcrModelRoot));
                Assert.False(Directory.Exists(missingPaths.ToolRoot));
            }
            finally
            {
                Environment.SetEnvironmentVariable(ChromiumHtmlToPdfService.ChromiumExecutableEnvironmentVariable, previousBrowser);
                Environment.SetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable, previousCdpEndpoint);
                Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", previousOcr);
                Environment.SetEnvironmentVariable(PostgreSqlToolLocator.BinRootEnvironmentVariable, previousPostgreSql);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Fact]
        public void Inspect_ShouldTreatConfiguredIsolatedBrowserAsReadyWithoutLocalExecutable()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "runtime-dependency-cdp-tests", Guid.NewGuid().ToString("N"));
            string previousEndpoint = Environment.GetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(
                    BrowserCdpEndpointPolicy.EndpointEnvironmentVariable,
                    "http://browser:9222/");
                var diagnostics = new RuntimeDependencyDiagnosticsService(
                    new RuntimeAppPathProvider(Path.Combine(root, "app"), Path.Combine(root, "data")))
                    .Inspect();

                var renderer = Assert.Single(diagnostics, item => item.Key == "report-renderer");
                var automation = Assert.Single(diagnostics, item => item.Key == "browser-automation");
                Assert.True(renderer.Ready);
                Assert.True(automation.Ready);
                Assert.Equal("http://browser:9222", renderer.ResolvedPath);
                Assert.Equal("http://browser:9222", automation.ResolvedPath);
                Assert.Contains("隔离", renderer.Message, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable, previousEndpoint);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Theory]
        [InlineData("http://browser:9222", true)]
        [InlineData("http://127.0.0.1:9222/", true)]
        [InlineData("https://browser.example.com:9443", true)]
        [InlineData("http://browser.example.com:9222", false)]
        [InlineData("ftp://browser:9222", false)]
        [InlineData("http://user:password@browser:9222", false)]
        [InlineData("http://browser:9222/json/version", false)]
        public void BrowserCdpEndpointPolicy_ShouldAcceptOnlyExplicitTrustedEndpoints(
            string configured,
            bool expectedValid)
        {
            string previousEndpoint = Environment.GetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable, configured);
                if (expectedValid)
                {
                    Assert.True(BrowserCdpEndpointPolicy.TryResolve(out Uri endpoint));
                    Assert.Equal(configured.TrimEnd('/'), endpoint.ToString().TrimEnd('/'));
                }
                else
                {
                    Assert.Throws<ServiceValidationException>(() => BrowserCdpEndpointPolicy.TryResolve(out _));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(BrowserCdpEndpointPolicy.EndpointEnvironmentVariable, previousEndpoint);
            }
        }

        [Theory]
        [InlineData("win64", "v8_context_snapshot.bin")]
        [InlineData("linux64", "v8_context_snapshot.bin")]
        [InlineData("mac-arm64", "v8_context_snapshot.arm64.bin")]
        [InlineData("mac-x64", "v8_context_snapshot.x86_64.bin")]
        public void HeadlessShellRequiredFiles_ShouldMatchOfficialPlatformArchive(
            string runtimePlatform,
            string expectedSnapshotFile)
        {
            IReadOnlyList<string> requiredFiles =
                BrowserExecutableResolver.GetRequiredHeadlessShellFiles(runtimePlatform);

            Assert.Contains("icudtl.dat", requiredFiles);
            Assert.Contains(expectedSnapshotFile, requiredFiles);
            Assert.Contains("headless_lib_data.pak", requiredFiles);
            Assert.Contains("headless_lib_strings.pak", requiredFiles);
            Assert.Equal(4, requiredFiles.Count);
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

        private static void WriteHeadlessShellBundle(string executablePath)
        {
            string root = Path.GetDirectoryName(executablePath)!;
            Directory.CreateDirectory(root);
            File.WriteAllText(executablePath, "test-browser");
            foreach (string fileName in BrowserExecutableResolver.GetRequiredHeadlessShellFiles(
                         BrowserExecutableResolver.GetRuntimePlatform()))
            {
                File.WriteAllText(Path.Combine(root, fileName), "test-resource");
            }

            string localesRoot = Path.Combine(root, "locales");
            Directory.CreateDirectory(localesRoot);
            File.WriteAllText(Path.Combine(localesRoot, "en-US.pak"), "test-locale");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executablePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }
}

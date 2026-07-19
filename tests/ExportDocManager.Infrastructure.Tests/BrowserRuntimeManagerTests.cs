using System.Diagnostics;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.MasterData;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class BrowserRuntimeManagerTests
    {
        [Fact]
        public async Task AutomationLease_ShouldSerializeAndReleaseCapacity()
        {
            string previous = Environment.GetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable);
            Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, "1");
            try
            {
                await using var runtime = new BrowserRuntimeManager();
                var first = await runtime.AcquireAsync(BrowserWorkloadKind.WebAutomation);
                var secondTask = runtime.AcquireAsync(BrowserWorkloadKind.WebAutomation);
                await Task.Delay(100);
                Assert.False(secondTask.IsCompleted);
                await first.DisposeAsync();
                await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(1, runtime.GetSnapshot().ActiveAutomationTasks);
            }
            finally
            {
                Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, previous);
            }
        }

        [Fact]
        public async Task OwnedProcessRegistration_ShouldOnlyTrackExplicitProcess()
        {
            await using var runtime = new BrowserRuntimeManager();
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            using (runtime.RegisterOwnedProcess(process, BrowserWorkloadKind.PdfRendering, "test"))
            {
                Assert.Contains(process.Id, runtime.GetSnapshot().OwnedProcessIds);
                await process.WaitForExitAsync();
            }
            Assert.DoesNotContain(process.Id, runtime.GetSnapshot().OwnedProcessIds);
        }

        [Fact]
        public async Task ManagedPlaywrightHost_ShouldUseBundledBrowserAndLeaveNoOwnedProcess()
        {
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(root, ".codex-runtime", "BrowserRuntimeManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            await using var runtime = new BrowserRuntimeManager();
            await using var host = new ManagedPlaywrightBrowserHost(runtime, new BrowserExecutableResolver(pathProvider), pathProvider);
            try
            {
                string value = await host.ExecuteAsync(async (page, cancellationToken) =>
                {
                    await page.SetContentAsync("<html><body><strong id='value'>browser-ok</strong></body></html>");
                    cancellationToken.ThrowIfCancellationRequested();
                    return await page.Locator("#value").InnerTextAsync();
                });
                Assert.Equal("browser-ok", value);
                Assert.Single(runtime.GetSnapshot().OwnedProcessIds);
            }
            finally
            {
                await host.DisposeAsync();
                Assert.Empty(runtime.GetSnapshot().OwnedProcessIds);
                if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
            }
        }

        [Fact]
        public async Task ChromiumPdfRenderer_ShouldReleaseLeaseAndOwnedProcess()
        {
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(root, ".codex-runtime", "BrowserRuntimeManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            await using var runtime = new BrowserRuntimeManager();
            var renderer = new ChromiumHtmlToPdfService(pathProvider, runtime);
            string destination = Path.Combine(dataRoot, "pdf", "runtime-managed.pdf");
            try
            {
                var result = await renderer.RenderAsync("<html><body>managed pdf</body></html>", destination);
                Assert.True(File.Exists(result.DestinationPath));
                var snapshot = runtime.GetSnapshot();
                Assert.Equal(0, snapshot.ActivePdfTasks);
                Assert.Empty(snapshot.OwnedProcessIds);
            }
            finally
            {
                if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
            }
        }

        [Fact]
        public async Task I5a6BrowserParser_ShouldReadDynamicSearchTable()
        {
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(root, ".codex-runtime", "BrowserRuntimeManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            await using var runtime = new BrowserRuntimeManager();
            await using var host = new ManagedPlaywrightBrowserHost(runtime, new BrowserExecutableResolver(pathProvider), pathProvider);
            try
            {
                var rows = await host.ExecuteAsync(async (page, _) =>
                {
                    await page.SetContentAsync("""
                        <table><tr><th>HS编码</th><th>商品名称</th></tr>
                        <tr><td><a href="https://www.i5a6.com/hscode/detail/1">8517130000</a></td><td>智能手机</td></tr></table>
                        """);
                    return await I5a6HsCodeProvider.ParseSearchPageAsync(page);
                });
                var item = Assert.Single(rows);
                Assert.Equal("8517130000", item.Code);
                Assert.Equal("智能手机", item.Name);
                Assert.Equal("i5a6（浏览器降级）", item.SourceName);
            }
            finally
            {
                await host.DisposeAsync();
                if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
}

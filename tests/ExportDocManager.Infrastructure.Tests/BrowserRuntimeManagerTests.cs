using System.Diagnostics;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Utils;

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
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public async Task ManagedPlaywrightHost_ShouldDeferRecycleUntilParallelPagesFinish()
        {
            string previousConcurrency = Environment.GetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable);
            string previousRecycleUses = Environment.GetEnvironmentVariable(ManagedPlaywrightBrowserHost.RecycleUsesEnvironmentVariable);
            Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, "2");
            Environment.SetEnvironmentVariable(ManagedPlaywrightBrowserHost.RecycleUsesEnvironmentVariable, "1");
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(root, ".codex-runtime", "BrowserRuntimeManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            try
            {
                await using var runtime = new BrowserRuntimeManager();
                await using var host = new ManagedPlaywrightBrowserHost(runtime, new BrowserExecutableResolver(pathProvider), pathProvider);
                var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = host.ExecuteAsync(async (page, cancellationToken) =>
                {
                    await page.SetContentAsync("<div id='first'>still-alive</div>");
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                    return await page.Locator("#first").InnerTextAsync();
                });
                await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));

                string second = await host.ExecuteAsync(async (page, _) =>
                {
                    await page.SetContentAsync("<div id='second'>done</div>");
                    return await page.Locator("#second").InnerTextAsync();
                });

                Assert.Equal("done", second);
                Assert.Single(runtime.GetSnapshot().OwnedProcessIds);
                releaseFirst.TrySetResult(true);
                Assert.Equal("still-alive", await first.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Empty(runtime.GetSnapshot().OwnedProcessIds);
            }
            finally
            {
                Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, previousConcurrency);
                Environment.SetEnvironmentVariable(ManagedPlaywrightBrowserHost.RecycleUsesEnvironmentVariable, previousRecycleUses);
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
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
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
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
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public async Task I5a6BrowserParser_ShouldPreferTableWithDetailLinksAndReadSpecification()
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
                        <table><tr><td>HS编码</td><td>没有结果的标准编码表</td></tr></table>
                        <div id="hscasefind"><table>
                        <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
                        <tr><td><a href="//www.i5a6.com/hscode/detail/6109100010">61091000.10</a></td><td>棉制男T恤</td><td>针织|男式|100%棉</td></tr>
                        </table></div>
                        """);
                    return await I5a6HsCodeProvider.ParseSearchPageAsync(page);
                });
                var item = Assert.Single(rows);
                Assert.Equal("6109100010", item.Code);
                Assert.Equal("棉制男T恤", item.Name);
                Assert.Equal("针织|男式|100%棉", item.Description);
                Assert.Equal("https://www.i5a6.com/hscode/detail/6109100010", item.DetailUrl);
            }
            finally
            {
                await host.DisposeAsync();
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public async Task I5a6BrowserParser_ShouldReadMobileDealCards()
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
                        <a class="react" href="//www.i5a6.com/hscode/detail/6109100010">
                          <div class="dealcard react">
                            <div class="dealcard-brand single-line"><b>61091000.10</b></div>
                            <div class="title text-block">棉制男T恤</div>
                            <div class="title text-block">针织|男式|100%棉</div>
                          </div>
                        </a>
                        <a class="react" href="//www.i5a6.com/hscode/detail/6109100010">
                          <div class="dealcard react">
                            <div class="dealcard-brand single-line"><b>61091000.10</b></div>
                            <div class="title text-block">棉制针织男T恤衫</div>
                            <div class="title text-block">针织|男式|62%棉38%涤</div>
                          </div>
                        </a>
                        """);
                    return await I5a6HsCodeProvider.ParseSearchPageAsync(page);
                });
                Assert.Equal(2, rows.Count);
                Assert.All(rows, item => Assert.Equal("6109100010", item.Code));
                Assert.Contains(rows, item => item.Name == "棉制男T恤" && item.Description == "针织|男式|100%棉");
                Assert.Contains(rows, item => item.Name == "棉制针织男T恤衫" && item.Description == "针织|男式|62%棉38%涤");
            }
            finally
            {
                await host.DisposeAsync();
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
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

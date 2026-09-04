using System.ComponentModel;
using System.Diagnostics;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Utils;
using SkiaSharp;
using UglyToad.PdfPig;

namespace ExportDocManager.Infrastructure.Tests
{
    [Collection(BrowserIntegrationCollection.Name)]
    public sealed class BrowserRuntimeManagerTests
    {
        [Fact]
        public async Task AutomationLease_ShouldSerializeAndReleaseCapacity()
        {
            string? previous = Environment.GetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable);
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
        public async Task QueuedAutomationLease_ShouldNotBlockAvailablePdfCapacity()
        {
            string? previousGlobal = Environment.GetEnvironmentVariable(BrowserRuntimeManager.GlobalConcurrencyEnvironmentVariable);
            string? previousPdf = Environment.GetEnvironmentVariable(BrowserRuntimeManager.PdfConcurrencyEnvironmentVariable);
            string? previousAutomation = Environment.GetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable);
            Environment.SetEnvironmentVariable(BrowserRuntimeManager.GlobalConcurrencyEnvironmentVariable, "2");
            Environment.SetEnvironmentVariable(BrowserRuntimeManager.PdfConcurrencyEnvironmentVariable, "2");
            Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, "1");
            try
            {
                await using var runtime = new BrowserRuntimeManager();
                await using var firstAutomation = await runtime.AcquireAsync(BrowserWorkloadKind.WebAutomation);
                Task<BrowserWorkloadLease> queuedAutomation = runtime.AcquireAsync(BrowserWorkloadKind.WebAutomation);
                await Task.Delay(100);
                Assert.False(queuedAutomation.IsCompleted);

                await using var pdf = await runtime
                    .AcquireAsync(BrowserWorkloadKind.PdfRendering)
                    .WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(1, runtime.GetSnapshot().ActivePdfTasks);

                await firstAutomation.DisposeAsync();
                await using var secondAutomation = await queuedAutomation.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                Environment.SetEnvironmentVariable(BrowserRuntimeManager.GlobalConcurrencyEnvironmentVariable, previousGlobal);
                Environment.SetEnvironmentVariable(BrowserRuntimeManager.PdfConcurrencyEnvironmentVariable, previousPdf);
                Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, previousAutomation);
            }
        }

        [Fact]
        public async Task DisposeAsync_ShouldCancelQueuedAcquireAndWaitForActiveLease()
        {
            string? previousAutomation = Environment.GetEnvironmentVariable(
                BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable,
                "1");
            try
            {
                var runtime = new BrowserRuntimeManager();
                BrowserWorkloadLease activeLease = await runtime.AcquireAsync(
                    BrowserWorkloadKind.WebAutomation);
                Task<BrowserWorkloadLease> queuedAcquire = runtime.AcquireAsync(
                    BrowserWorkloadKind.WebAutomation);
                await Task.Delay(100);
                Assert.False(queuedAcquire.IsCompleted);

                Task disposeTask = runtime.DisposeAsync().AsTask();
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await queuedAcquire.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.False(disposeTask.IsCompleted);

                await activeLease.DisposeAsync();
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(0, runtime.GetSnapshot().ActiveAutomationTasks);
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => runtime.AcquireAsync(BrowserWorkloadKind.WebAutomation));
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable,
                    previousAutomation);
            }
        }

        [Theory]
        [InlineData("file:///runtime-data/report.html", BrowserNavigationPolicy.LocalFilesOnly, true)]
        [InlineData("data:text/html,ok", BrowserNavigationPolicy.LocalFilesOnly, true)]
        [InlineData("https://www.i5a6.com/hscode/search", BrowserNavigationPolicy.LocalFilesOnly, false)]
        [InlineData("https://www.i5a6.com/hscode/search", BrowserNavigationPolicy.I5a6Only, true)]
        [InlineData("https://www.i5a6.com:443/hscode/search", BrowserNavigationPolicy.I5a6Only, true)]
        [InlineData("https://user@www.i5a6.com/hscode/search", BrowserNavigationPolicy.I5a6Only, false)]
        [InlineData("http://www.i5a6.com/hscode/search", BrowserNavigationPolicy.I5a6Only, false)]
        [InlineData("https://www.i5a6.com.evil.test/hscode/search", BrowserNavigationPolicy.I5a6Only, false)]
        public void NavigationPolicy_ShouldKeepPdfLocalAndHsAutomationOnItsTrustedOrigin(
            string url,
            BrowserNavigationPolicy policy,
            bool expected)
        {
            Assert.Equal(expected, ManagedPlaywrightBrowserHost.IsNavigationAllowed(url, policy));
        }

        [Theory]
        [InlineData(5, true)]
        [InlineData(32, true)]
        [InlineData(33, true)]
        [InlineData(2, false)]
        [InlineData(193, false)]
        public void BrowserStartupRetry_ShouldOnlyTreatTemporaryWindowsProcessErrorsAsTransient(
            int nativeErrorCode,
            bool expected)
        {
            Assert.Equal(
                expected,
                ManagedPlaywrightBrowserHost.IsTransientStartupFailure(new Win32Exception(nativeErrorCode)));
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
        [Trait("Category", BrowserIntegrationCollection.Category)]
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
        [Trait("Category", BrowserIntegrationCollection.Category)]
        public async Task ManagedPlaywrightHost_ShouldDeferRecycleUntilParallelPagesFinish()
        {
            string? previousConcurrency = Environment.GetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable);
            string? previousRecycleUses = Environment.GetEnvironmentVariable(ManagedPlaywrightBrowserHost.RecycleUsesEnvironmentVariable);
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
                await WaitUntilAsync(
                    () => runtime.GetSnapshot().OwnedProcessIds.Count == 0,
                    TimeSpan.FromSeconds(15));
            }
            finally
            {
                Environment.SetEnvironmentVariable(BrowserRuntimeManager.AutomationConcurrencyEnvironmentVariable, previousConcurrency);
                Environment.SetEnvironmentVariable(ManagedPlaywrightBrowserHost.RecycleUsesEnvironmentVariable, previousRecycleUses);
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        [Trait("Category", BrowserIntegrationCollection.Category)]
        public async Task ManagedPlaywrightHost_ShouldRecycleLocalBrowserAfterIdleTimeout()
        {
            string? previousIdleTimeout = Environment.GetEnvironmentVariable(
                ManagedPlaywrightBrowserHost.IdleTimeoutSecondsEnvironmentVariable);
            Environment.SetEnvironmentVariable(
                ManagedPlaywrightBrowserHost.IdleTimeoutSecondsEnvironmentVariable,
                "1");
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(
                root,
                ".codex-runtime",
                "BrowserRuntimeManagerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            try
            {
                await using var runtime = new BrowserRuntimeManager();
                await using var host = new ManagedPlaywrightBrowserHost(
                    runtime,
                    new BrowserExecutableResolver(pathProvider),
                    pathProvider);

                string value = await host.ExecuteAsync(async (page, _) =>
                {
                    await page.SetContentAsync("<div id='idle'>ready</div>");
                    return await page.Locator("#idle").InnerTextAsync();
                });

                Assert.Equal("ready", value);
                Assert.Single(runtime.GetSnapshot().OwnedProcessIds);
                await WaitUntilAsync(
                    () => runtime.GetSnapshot().OwnedProcessIds.Count == 0,
                    TimeSpan.FromSeconds(15));
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    ManagedPlaywrightBrowserHost.IdleTimeoutSecondsEnvironmentVariable,
                    previousIdleTimeout);
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        [Trait("Category", BrowserIntegrationCollection.Category)]
        public async Task ManagedPlaywrightHost_DisposeAsync_ShouldCancelActivePageAndRejectNewWork()
        {
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(
                root,
                ".codex-runtime",
                "BrowserRuntimeManagerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            await using var runtime = new BrowserRuntimeManager();
            var host = new ManagedPlaywrightBrowserHost(
                runtime,
                new BrowserExecutableResolver(pathProvider),
                pathProvider);
            try
            {
                var started = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Task<string> activeOperation = host.ExecuteAsync(async (page, cancellationToken) =>
                {
                    await page.SetContentAsync("<div>active</div>");
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return "unreachable";
                });
                await started.Task.WaitAsync(TimeSpan.FromSeconds(20));

                Task disposeTask = host.DisposeAsync().AsTask();
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await activeOperation.WaitAsync(TimeSpan.FromSeconds(20)));
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Empty(runtime.GetSnapshot().OwnedProcessIds);
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    () => host.ExecuteAsync((_, _) => Task.FromResult("late")));
            }
            finally
            {
                await host.DisposeAsync();
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        [Trait("Category", BrowserIntegrationCollection.Category)]
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
        [Trait("Category", BrowserIntegrationCollection.Category)]
        public async Task ChromiumPdfRenderer_ShouldDecodeDataImageBeforePrinting()
        {
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(root, ".codex-runtime", "BrowserRuntimeManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            await using var runtime = new BrowserRuntimeManager();
            var renderer = new ChromiumHtmlToPdfService(pathProvider, runtime);
            string destination = Path.Combine(dataRoot, "pdf", "image-ready.pdf");
            try
            {
                using var bitmap = new SKBitmap(734, 424, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var random = new Random(20260804);
                byte[] pixels = new byte[bitmap.ByteCount];
                random.NextBytes(pixels);
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                string dataUri = $"data:image/png;base64,{Convert.ToBase64String(png.ToArray())}";

                await renderer.RenderAsync($"<html><body><img src=\"{dataUri}\" style=\"width:220px;height:130px\"></body></html>", destination);

                using var document = PdfDocument.Open(destination);
                Assert.True(document.GetPage(1).NumberOfImages > 0, "Decoded report image must be embedded in the PDF.");
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        [Trait("Category", BrowserIntegrationCollection.Category)]
        public async Task ChromiumPdfRenderer_ShouldMaterializeV3PageNumbers()
        {
            string root = FindRepositoryRoot();
            string dataRoot = Path.Combine(root, ".codex-runtime", "BrowserRuntimeManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            var pathProvider = new RuntimeAppPathProvider(root, dataRoot);
            await using var runtime = new BrowserRuntimeManager();
            var renderer = new ChromiumHtmlToPdfService(pathProvider, runtime);
            string destination = Path.Combine(dataRoot, "pdf", "v3-page-numbers.pdf");
            string rows = string.Concat(Enumerable.Repeat("<div style=\"height:18mm\">PAGE-CONTENT</div>", 80));
            string html = """
                <!doctype html><html><head><style>
                @page { size: 210mm 297mm; margin: 0; }
                html, body { margin: 0; padding: 0; }
                .edm-v3-page { position: relative; width: 210mm; min-height: 297mm; }
                .edm-v3-repeat-layer { position: fixed; inset: 0; width: 210mm; height: 297mm; }
                .edm-v3-element { position: absolute; }
                </style></head><body>
                <div class="edm-v3-page">__ROWS__</div>
                <div class="edm-v3-repeat-layer">
                  <div class="edm-v3-element" style="left:10mm;top:285mm;width:100mm;height:6mm">
                    <span data-edm-v3-page-number>PG <span data-edm-v3-page-number-current>1</span> / <span data-edm-v3-page-number-total>1</span> END</span>
                  </div>
                </div>
                </body></html>
                """;
            html = html.Replace("__ROWS__", rows, StringComparison.Ordinal);
            try
            {
                await renderer.RenderAsync(html, destination);
                using var document = PdfDocument.Open(destination);
                Assert.True(document.NumberOfPages >= 4, "V3 page-number fixture must span multiple pages.");
                var pageTexts = document.GetPages().Select(page => page.Text).ToArray();
                Console.WriteLine(string.Join("\n---PAGE---\n", pageTexts));
                Assert.DoesNotContain(pageTexts, text => text.Contains("0 / 0", StringComparison.Ordinal));
                for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
                {
                    Assert.Contains($"PG {pageNumber} / {document.NumberOfPages} END", pageTexts[pageNumber - 1], StringComparison.Ordinal);
                }
            }
            finally
            {
                // Keep the fixture temporarily while diagnosing page-number layout.
            }
        }

        [Fact]
        [Trait("Category", BrowserIntegrationCollection.Category)]
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
                    return await I5a6HsCodeProvider.ParseSearchPageAsync(page, TimeProvider.System);
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
        [Trait("Category", BrowserIntegrationCollection.Category)]
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
                    return await I5a6HsCodeProvider.ParseSearchPageAsync(page, TimeProvider.System);
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
        [Trait("Category", BrowserIntegrationCollection.Category)]
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
                    return await I5a6HsCodeProvider.ParseSearchPageAsync(page, TimeProvider.System);
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

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            while (!predicate())
            {
                await Task.Delay(100, timeoutSource.Token);
            }
        }
    }
}

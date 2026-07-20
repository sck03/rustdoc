using System.Diagnostics;
using Microsoft.Playwright;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.BrowserRuntime
{
    public sealed class ManagedPlaywrightBrowserHost : IAsyncDisposable, IDisposable
    {
        public const string TimeoutEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_TIMEOUT_SECONDS";
        public const string RecycleUsesEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_RECYCLE_USES";
        public const string RecycleMinutesEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_RECYCLE_MINUTES";

        private readonly BrowserRuntimeManager _runtime;
        private readonly BrowserExecutableResolver _resolver;
        private readonly IAppPathProvider _pathProvider;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private IPlaywright _playwright;
        private IBrowser _browser;
        private Process _process;
        private BrowserProcessRegistration _registration;
        private string _profileRoot;
        private DateTimeOffset _startedAt;
        private int _useCount;
        private int _disposed;

        public ManagedPlaywrightBrowserHost(
            BrowserRuntimeManager runtime,
            BrowserExecutableResolver resolver,
            IAppPathProvider pathProvider)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public string GetAvailabilityMessage()
        {
            try
            {
                string executable = _resolver.Resolve();
                return $"受控浏览器降级可用：{Path.GetFileName(executable)}；当前归属进程 {_runtime.GetSnapshot().OwnedProcessIds.Count} 个。";
            }
            catch (Exception ex)
            {
                return $"受控浏览器降级不可用：{ex.Message}";
            }
        }

        public async Task<T> ExecuteAsync<T>(
            Func<IPage, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await using var workload = await _runtime.AcquireAsync(BrowserWorkloadKind.WebAutomation, cancellationToken).ConfigureAwait(false);
            TimeSpan timeout = TimeSpan.FromSeconds(ReadPositiveInt(TimeoutEnvironmentVariable, 30, 5, 180));
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            IBrowserContext context = null;
            IPage page = null;
            try
            {
                IBrowser browser = await GetOrCreateBrowserAsync(linkedCts.Token).ConfigureAwait(false);
                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = false,
                    Locale = "zh-CN",
                    UserAgent = "Mozilla/5.0 AppleWebKit/537.36 Chrome/151.0 Safari/537.36 ExportDocManager-HsCode"
                }).ConfigureAwait(false);
                page = await context.NewPageAsync().ConfigureAwait(false);
                page.SetDefaultTimeout((float)timeout.TotalMilliseconds);
                page.SetDefaultNavigationTimeout((float)timeout.TotalMilliseconds);
                T result = await operation(page, linkedCts.Token).ConfigureAwait(false);
                Interlocked.Increment(ref _useCount);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                await InvalidateAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"浏览器自动化超过 {Math.Ceiling(timeout.TotalSeconds)} 秒，已关闭本任务页面并回收受控浏览器进程。");
            }
            catch
            {
                if (_process == null || _process.HasExited || _browser == null || !_browser.IsConnected)
                    await InvalidateAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (page != null) try { await page.CloseAsync().ConfigureAwait(false); } catch { }
                if (context != null) try { await context.CloseAsync().ConfigureAwait(false); } catch { }
                if (ShouldRecycle()) await InvalidateAsync().ConfigureAwait(false);
            }
        }

        private async Task<IBrowser> GetOrCreateBrowserAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_browser != null && _browser.IsConnected && _process != null && !_process.HasExited) return _browser;
                await StopBrowserCoreAsync().ConfigureAwait(false);
                string executable = _resolver.Resolve();
                _profileRoot = Path.Combine(_pathProvider.CacheRoot, "BrowserRuntime", $"automation-{Environment.ProcessId}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(_profileRoot);
                var endpointSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };
                AddBrowserArguments(process.StartInfo, _profileRoot);
                process.ErrorDataReceived += (_, args) =>
                {
                    const string marker = "DevTools listening on ";
                    int markerIndex = args.Data?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
                    if (markerIndex >= 0) endpointSource.TrySetResult(args.Data![(markerIndex + marker.Length)..].Trim());
                };
                process.Exited += (_, _) => endpointSource.TrySetException(new InvalidOperationException("受控 Chromium 在建立自动化连接前退出。"));
                if (!process.Start()) throw new InvalidOperationException("无法启动受控 Chromium 自动化进程。");
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                _process = process;
                _registration = _runtime.RegisterOwnedProcess(process, BrowserWorkloadKind.WebAutomation, "Playwright HS lookup");
                string endpoint = await endpointSource.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                _browser = await _playwright.Chromium.ConnectOverCDPAsync(endpoint).ConfigureAwait(false);
                _startedAt = DateTimeOffset.Now;
                _useCount = 0;
                return _browser;
            }
            catch
            {
                await StopBrowserCoreAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private static void AddBrowserArguments(ProcessStartInfo startInfo, string profileRoot)
        {
            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--remote-debugging-port=0");
            startInfo.ArgumentList.Add($"--user-data-dir={profileRoot}");
            startInfo.ArgumentList.Add("--disable-gpu");
            startInfo.ArgumentList.Add("--disable-extensions");
            startInfo.ArgumentList.Add("--disable-background-networking");
            startInfo.ArgumentList.Add("--disable-dev-shm-usage");
            startInfo.ArgumentList.Add("--disable-sync");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add("--disable-features=Translate,MediaRouter,OptimizationHints");
            if (OperatingSystem.IsLinux() && string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase))
                startInfo.ArgumentList.Add("--no-sandbox");
            startInfo.ArgumentList.Add("about:blank");
        }

        private bool ShouldRecycle()
        {
            if (_browser == null) return false;
            int maxUses = ReadPositiveInt(RecycleUsesEnvironmentVariable, 100, 1, 1000);
            int maxMinutes = ReadPositiveInt(RecycleMinutesEnvironmentVariable, 30, 1, 240);
            return Volatile.Read(ref _useCount) >= maxUses || DateTimeOffset.Now - _startedAt >= TimeSpan.FromMinutes(maxMinutes);
        }

        private async Task InvalidateAsync()
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try { await StopBrowserCoreAsync().ConfigureAwait(false); }
            finally { _lifecycleGate.Release(); }
        }

        private async Task StopBrowserCoreAsync()
        {
            if (_browser != null) try { await _browser.CloseAsync().ConfigureAwait(false); } catch { }
            _browser = null;
            _playwright?.Dispose();
            _playwright = null;
            if (_process != null)
            {
                await BrowserRuntimeManager.KillOwnedProcessAsync(_process).ConfigureAwait(false);
                _registration?.Dispose();
                _registration = null;
                _process.Dispose();
                _process = null;
            }
            if (!string.IsNullOrWhiteSpace(_profileRoot))
            {
                for (int attempt = 0; attempt < 5 && Directory.Exists(_profileRoot); attempt++)
                {
                    AtomicFileHelper.TryDeleteDirectory(_profileRoot);
                    if (Directory.Exists(_profileRoot)) await Task.Delay(200).ConfigureAwait(false);
                }
            }
            _profileRoot = null;
            _useCount = 0;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await InvalidateAsync().ConfigureAwait(false);
            _lifecycleGate.Dispose();
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private static int ReadPositiveInt(string name, int fallback, int minimum, int maximum)
        {
            string value = Environment.GetEnvironmentVariable(name) ?? string.Empty;
            return int.TryParse(value.Trim(), out int parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
        }
    }
}

using System.Diagnostics;
using Microsoft.Playwright;
using ExportDocManager.Services.Errors;
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
        private int _activeOperations;
        private bool _recycleRequested;
        private int _disposed;

        public ManagedPlaywrightBrowserHost(
            BrowserRuntimeManager runtime,
            BrowserExecutableResolver resolver,
            IAppPathProvider pathProvider)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            CleanupStaleProfiles();
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
            TimeSpan timeout = TimeSpan.FromSeconds(ReadPositiveInt(TimeoutEnvironmentVariable, 30, 5, 180));
            return await ExecuteAsync(
                    BrowserWorkloadKind.WebAutomation,
                    timeout,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        internal async Task<T> ExecuteAsync<T>(
            BrowserWorkloadKind workload,
            TimeSpan timeout,
            Func<IPage, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await using var workloadLease = await _runtime.AcquireAsync(workload, cancellationToken).ConfigureAwait(false);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            IBrowserContext context = null;
            IPage page = null;
            bool recycleBrowser = false;
            bool operationStarted = false;
            try
            {
                IBrowser browser = await BeginOperationAsync(workload, linkedCts.Token).ConfigureAwait(false);
                operationStarted = true;
                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = false,
                    Locale = "zh-CN",
                    UserAgent = "Mozilla/5.0 AppleWebKit/537.36 Chrome/151.0 Safari/537.36 ExportDocManager-BrowserRuntime"
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
                recycleBrowser = true;
                string operationName = workload == BrowserWorkloadKind.PdfRendering ? "PDF 渲染" : "自动化";
                throw new ServiceTimeoutException($"浏览器{operationName}超过 {Math.Ceiling(timeout.TotalSeconds)} 秒，已关闭本任务页面；受控浏览器将在其他并行页面结束后安全回收。");
            }
            catch
            {
                if (_process == null || _process.HasExited || _browser == null || !_browser.IsConnected)
                    recycleBrowser = true;
                throw;
            }
            finally
            {
                if (page != null) try { await page.CloseAsync().ConfigureAwait(false); } catch { }
                if (context != null) try { await context.CloseAsync().ConfigureAwait(false); } catch { }
                if (operationStarted)
                {
                    await EndOperationAsync(recycleBrowser || ShouldRecycle()).ConfigureAwait(false);
                }
            }
        }

        private async Task<IBrowser> BeginOperationAsync(
            BrowserWorkloadKind workload,
            CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_activeOperations == 0 && (_recycleRequested || ShouldRecycle()))
                {
                    await StopBrowserCoreAsync().ConfigureAwait(false);
                    _recycleRequested = false;
                }

                if (_browser == null || !_browser.IsConnected || _process == null || _process.HasExited)
                {
                    await StopBrowserCoreAsync().ConfigureAwait(false);
                    await StartBrowserCoreAsync(workload, cancellationToken).ConfigureAwait(false);
                }

                _activeOperations++;
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

        private async Task EndOperationAsync(bool requestRecycle)
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                _recycleRequested |= requestRecycle;
                _activeOperations = Math.Max(0, _activeOperations - 1);
                if (_activeOperations == 0 && _recycleRequested)
                {
                    await StopBrowserCoreAsync().ConfigureAwait(false);
                    _recycleRequested = false;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task StartBrowserCoreAsync(
            BrowserWorkloadKind workload,
            CancellationToken cancellationToken)
        {
            try
            {
                await StartBrowserProcessCoreAsync(workload, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InfrastructureServiceException("受控 Chromium 启动或连接失败。", ex);
            }
        }

        private async Task StartBrowserProcessCoreAsync(
            BrowserWorkloadKind workload,
            CancellationToken cancellationToken)
        {
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
            foreach (string argument in BuildBrowserArguments(
                         _profileRoot,
                         ChromiumSandboxPolicy.ResolveNoSandboxSetting(),
                         ChromiumSharedMemoryPolicy.ResolveDisableDevShmUsageSetting()))
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.ErrorDataReceived += (_, args) =>
            {
                const string marker = "DevTools listening on ";
                int markerIndex = args.Data?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
                if (markerIndex >= 0) endpointSource.TrySetResult(args.Data![(markerIndex + marker.Length)..].Trim());
            };
            process.Exited += (_, _) => endpointSource.TrySetException(
                new InfrastructureServiceException("受控 Chromium 在建立连接前退出。"));
            if (!process.Start()) throw new InfrastructureServiceException("无法启动受控 Chromium 进程。");
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            _process = process;
            _registration = _runtime.RegisterOwnedProcess(process, workload, "Managed Chromium browser");
            string endpoint = await endpointSource.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            _browser = await _playwright.Chromium.ConnectOverCDPAsync(endpoint).ConfigureAwait(false);
            _startedAt = DateTimeOffset.Now;
            _useCount = 0;
        }

        internal static IReadOnlyList<string> BuildBrowserArguments(
            string profileRoot,
            bool disableSandbox,
            bool disableDevShmUsage = true)
        {
            var arguments = new List<string>
            {
                "--headless=new",
                "--remote-debugging-port=0",
                $"--user-data-dir={profileRoot}",
                "--disable-gpu",
                "--disable-extensions",
                "--disable-background-networking",
                "--disable-sync",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-features=Translate,MediaRouter,OptimizationHints",
                "--allow-file-access-from-files",
                "about:blank"
            };
            if (disableSandbox)
            {
                arguments.Insert(1, "--no-sandbox");
            }
            if (disableDevShmUsage)
            {
                arguments.Insert(1, "--disable-dev-shm-usage");
            }

            return arguments;
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

        private void CleanupStaleProfiles()
        {
            string runtimeRoot = Path.Combine(_pathProvider.CacheRoot, "BrowserRuntime");
            if (!Directory.Exists(runtimeRoot))
            {
                return;
            }

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(runtimeRoot, "automation-*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(directory);
                    string processIdPart = name.Split('-', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() ?? string.Empty;
                    if (!int.TryParse(processIdPart, out int processId) || processId == Environment.ProcessId || IsProcessRunning(processId))
                    {
                        continue;
                    }

                    AtomicFileHelper.TryDeleteDirectory(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Stale cache cleanup is best effort and must not prevent browser startup.
            }
        }

        private static bool IsProcessRunning(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
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

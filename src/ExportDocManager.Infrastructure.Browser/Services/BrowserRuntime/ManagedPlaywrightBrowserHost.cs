using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Playwright;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.BrowserRuntime
{
    public class ManagedPlaywrightBrowserHost : IAsyncDisposable, IDisposable
    {
        public const string TimeoutEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_TIMEOUT_SECONDS";
        public const string StartupTimeoutEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_STARTUP_TIMEOUT_SECONDS";
        public const string RecycleUsesEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_RECYCLE_USES";
        public const string RecycleMinutesEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_RECYCLE_MINUTES";
        public const string IdleTimeoutSecondsEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_IDLE_TIMEOUT_SECONDS";
        private static readonly TimeSpan BrowserShutdownTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LifecycleGateTimeout = TimeSpan.FromSeconds(20);

        private readonly BrowserRuntimeManager _runtime;
        private readonly BrowserExecutableResolver _resolver;
        private readonly IAppPathProvider _pathProvider;
        private readonly BrowserNavigationPolicy _navigationPolicy;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private readonly CancellationTokenSource _shutdownSource = new();
        private readonly AsyncIdleActionScheduler _idleRecycleScheduler;
        private readonly object _disposeSync = new();
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private Process? _process;
        private BrowserProcessRegistration? _registration;
        private string? _profileRoot;
        private string? _artifactsRoot;
        private DateTimeOffset _startedAt;
        private bool _remoteBrowser;
        private int _useCount;
        private int _activeOperations;
        private bool _recycleRequested;
        private bool _stopping;
        private int _disposed;
        private TaskCompletionSource<bool>? _operationsDrained;
        private Task? _disposeTask;

        public ManagedPlaywrightBrowserHost(
            BrowserRuntimeManager runtime,
            BrowserExecutableResolver resolver,
            IAppPathProvider pathProvider,
            BrowserNavigationPolicy navigationPolicy = BrowserNavigationPolicy.Unrestricted,
            TimeProvider? timeProvider = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _navigationPolicy = navigationPolicy;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _idleRecycleScheduler = new AsyncIdleActionScheduler(RecycleIdleBrowserAsync);
            CleanupStaleProfiles();
        }

        public string GetAvailabilityMessage()
        {
            try
            {
                if (BrowserCdpEndpointPolicy.TryResolve(out Uri? endpoint))
                {
                    return $"隔离浏览器服务已配置：{endpoint.GetLeftPart(UriPartial.Authority)}；" +
                           "Chromium 与数据库维护凭据不在同一进程。";
                }

                string executable = _resolver.Resolve();
                string sandboxState = ChromiumSandboxPolicy.ResolveNoSandboxSetting()
                    ? "已进入旧版系统/显式配置兼容模式"
                    : "已启用";
                return $"受控浏览器降级可用：{Path.GetFileName(executable)}；Chromium 沙箱{sandboxState}；当前归属进程 {_runtime.GetSnapshot().OwnedProcessIds.Count} 个。";
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
            using var acquireCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdownSource.Token);
            await using var workloadLease = await _runtime
                .AcquireAsync(workload, acquireCts.Token)
                .ConfigureAwait(false);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token,
                _shutdownSource.Token);

            IBrowserContext? context = null;
            IPage? page = null;
            bool recycleBrowser = false;
            bool operationStarted = false;
            try
            {
                IBrowser browser = await BeginOperationAsync(workload, linkedCts.Token).ConfigureAwait(false);
                operationStarted = true;
                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = false,
                    Locale = "zh-CN"
                })
                    .WaitAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                await ConfigureNavigationPolicyAsync(context, linkedCts.Token).ConfigureAwait(false);
                page = await context.NewPageAsync()
                    .WaitAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                page.SetDefaultTimeout((float)timeout.TotalMilliseconds);
                page.SetDefaultNavigationTimeout((float)timeout.TotalMilliseconds);
                T result = await operation(page, linkedCts.Token)
                    .WaitAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref _useCount);
                return result;
            }
            catch (OperationCanceledException) when (
                _shutdownSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                recycleBrowser = true;
                throw new ObjectDisposedException(
                    nameof(ManagedPlaywrightBrowserHost),
                    "受控浏览器正在停止，当前任务已取消。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                recycleBrowser = true;
                string operationName = workload == BrowserWorkloadKind.PdfRendering ? "PDF 渲染" : "自动化";
                throw new ServiceTimeoutException($"浏览器{operationName}超过 {Math.Ceiling(timeout.TotalSeconds)} 秒，已关闭本任务页面；受控浏览器将在其他并行页面结束后安全回收。");
            }
            catch
            {
                if (_browser == null ||
                    !_browser.IsConnected ||
                    (!_remoteBrowser && (_process == null || _process.HasExited)))
                {
                    recycleBrowser = true;
                }
                throw;
            }
            finally
            {
                if (page != null)
                {
                    try { await page.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
                }
                if (context != null)
                {
                    try { await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
                }
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
                _idleRecycleScheduler.Cancel();
                ObjectDisposedException.ThrowIf(
                    _stopping || Volatile.Read(ref _disposed) != 0,
                    this);
                if (_activeOperations == 0 && (_recycleRequested || ShouldRecycle()))
                {
                    await StopBrowserCoreAsync().ConfigureAwait(false);
                    _recycleRequested = false;
                }

                bool localProcessUnavailable =
                    !_remoteBrowser && (_process == null || _process.HasExited);
                if (_browser == null || !_browser.IsConnected || localProcessUnavailable)
                {
                    await StopBrowserCoreAsync().ConfigureAwait(false);
                    await StartBrowserCoreAsync(workload, cancellationToken).ConfigureAwait(false);
                }

                _activeOperations++;
                return _browser ?? throw new InfrastructureServiceException("受控浏览器启动后未建立连接。");
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
            bool drained = false;
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                _recycleRequested |= requestRecycle;
                _activeOperations = Math.Max(0, _activeOperations - 1);
                if (_activeOperations == 0 && _browser != null && !_remoteBrowser && !_stopping)
                {
                    TimeSpan delay = _recycleRequested
                        ? TimeSpan.FromMilliseconds(1)
                        : TimeSpan.FromSeconds(ReadPositiveInt(IdleTimeoutSecondsEnvironmentVariable, 120, 1, 3600));
                    _idleRecycleScheduler.Schedule(delay);
                }
                drained = _activeOperations == 0;
            }
            finally
            {
                _lifecycleGate.Release();
            }

            if (drained)
            {
                _operationsDrained?.TrySetResult(true);
            }
        }

        private async Task StartBrowserCoreAsync(
            BrowserWorkloadKind workload,
            CancellationToken cancellationToken)
        {
            int maximumAttempts = BrowserCdpEndpointPolicy.TryResolve(out _) ? 5 : 3;
            Exception? lastTransientError = null;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                try
                {
                    await StartBrowserProcessCoreAsync(workload, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransientStartupFailure(ex))
                {
                    lastTransientError = ex;
                    await StopBrowserCoreAsync().ConfigureAwait(false);
                    if (attempt < maximumAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken)
                            .ConfigureAwait(false);
                    }
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

            throw new InfrastructureServiceException(
                $"受控 Chromium 连续 {maximumAttempts} 次启动或连接失败。",
                lastTransientError);
        }

        internal static bool IsTransientStartupFailure(Exception exception) =>
            exception is TimeoutException or PlaywrightException or IOException ||
            exception is Win32Exception { NativeErrorCode: 5 or 32 or 33 };

        private async Task StartBrowserProcessCoreAsync(
            BrowserWorkloadKind workload,
            CancellationToken cancellationToken)
        {
            TimeSpan startupTimeout = TimeSpan.FromSeconds(
                ReadPositiveInt(StartupTimeoutEnvironmentVariable, 15, 5, 60));
            if (BrowserCdpEndpointPolicy.TryResolve(out Uri? remoteEndpoint))
            {
                await ConnectRemoteBrowserCoreAsync(
                        remoteEndpoint,
                        startupTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            string executable = _resolver.Resolve();
            string runDirectoryName = $"p-{Environment.ProcessId}-{Guid.NewGuid():N}"[..20];
            _profileRoot = Path.Combine(_pathProvider.CacheRoot, "Br", runDirectoryName);
            _artifactsRoot = Path.Combine(_pathProvider.CacheRoot, "Ba", runDirectoryName);
            Directory.CreateDirectory(_profileRoot);
            Directory.CreateDirectory(_artifactsRoot);
            var endpointSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    ErrorDialog = false,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                },
                EnableRaisingEvents = true
            };
            foreach (string argument in BuildBrowserArguments(
                         _profileRoot,
                         ChromiumSandboxPolicy.ResolveNoSandboxSetting(),
                         ChromiumSharedMemoryPolicy.ResolveDisableDevShmUsageSetting(),
                         allowFileAccessFromFiles: _navigationPolicy != BrowserNavigationPolicy.I5a6Only))
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
            if (!ChromiumProcessLauncher.Start(process)) throw new InfrastructureServiceException("无法启动受控 Chromium 进程。");
            _process = process;
            _registration = _runtime.RegisterOwnedProcess(process, workload, "Managed Chromium browser");
            // Register ownership before starting asynchronous pipe readers. If
            // reader initialization fails, StopBrowserCoreAsync can still find
            // and terminate the exact process tree that was started here.
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            string endpoint = await endpointSource.Task.WaitAsync(startupTimeout, cancellationToken).ConfigureAwait(false);
            _playwright = await Playwright.CreateAsync()
                .WaitAsync(startupTimeout, cancellationToken)
                .ConfigureAwait(false);
            _browser = await _playwright.Chromium.ConnectOverCDPAsync(
                    endpoint,
                    new BrowserTypeConnectOverCDPOptions
                    {
                        ArtifactsDir = _artifactsRoot,
                        IsLocal = true,
                        Timeout = (float)startupTimeout.TotalMilliseconds
                    })
                .WaitAsync(startupTimeout, cancellationToken)
                .ConfigureAwait(false);
            _remoteBrowser = false;
            _startedAt = _timeProvider.GetUtcNow();
            _useCount = 0;
        }

        private async Task ConnectRemoteBrowserCoreAsync(
            Uri endpoint,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken)
        {
            string runDirectoryName = $"p-{Environment.ProcessId}-{Guid.NewGuid():N}"[..20];
            _profileRoot = null;
            _artifactsRoot = Path.Combine(_pathProvider.CacheRoot, "Ba", runDirectoryName);
            Directory.CreateDirectory(_artifactsRoot);
            using var discoveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            discoveryTimeout.CancelAfter(startupTimeout);
            Uri webSocketEndpoint;
            try
            {
                webSocketEndpoint = await BrowserCdpConnectionResolver
                    .ResolveWebSocketEndpointAsync(endpoint, discoveryTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (
                !cancellationToken.IsCancellationRequested &&
                discoveryTimeout.IsCancellationRequested)
            {
                throw new TimeoutException("连接隔离 Chromium 服务超时。", ex);
            }
            _playwright = await Playwright.CreateAsync()
                .WaitAsync(startupTimeout, cancellationToken)
                .ConfigureAwait(false);
            _browser = await _playwright.Chromium.ConnectOverCDPAsync(
                    webSocketEndpoint.ToString(),
                    new BrowserTypeConnectOverCDPOptions
                    {
                        ArtifactsDir = _artifactsRoot,
                        IsLocal = false,
                        Timeout = (float)startupTimeout.TotalMilliseconds
                    })
                .WaitAsync(startupTimeout, cancellationToken)
                .ConfigureAwait(false);
            _remoteBrowser = true;
            _startedAt = _timeProvider.GetUtcNow();
            _useCount = 0;
        }

        internal static IReadOnlyList<string> BuildBrowserArguments(
            string profileRoot,
            bool disableSandbox,
            bool disableDevShmUsage = true,
            bool allowFileAccessFromFiles = true)
        {
            var arguments = new List<string>
            {
                "--headless",
                "--remote-debugging-port=0",
                "--remote-debugging-address=127.0.0.1",
                $"--user-data-dir={profileRoot}",
                "--disable-gpu",
                "--disable-field-trial-config",
                "--disable-background-networking",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-back-forward-cache",
                "--disable-breakpad",
                "--disable-client-side-phishing-detection",
                "--disable-component-extensions-with-background-pages",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-edgeupdater",
                "--disable-extensions",
                "--disable-hang-monitor",
                "--disable-ipc-flooding-protection",
                "--disable-popup-blocking",
                "--disable-prompt-on-repost",
                "--disable-renderer-backgrounding",
                "--disable-sync",
                "--force-color-profile=srgb",
                "--metrics-recording-only",
                "--no-first-run",
                "--no-default-browser-check",
                "--password-store=basic",
                "--use-mock-keychain",
                "--no-service-autorun",
                "--export-tagged-pdf",
                "--disable-search-engine-choice-screen",
                "--unsafely-disable-devtools-self-xss-warnings",
                "--edge-skip-compat-layer-relaunch",
                "--disable-infobars",
                "--allow-pre-commit-input",
                "--enable-features=CDPScreenshotNewSurface",
                "--disable-features=AvoidUnnecessaryBeforeUnloadCheckSync,BoundaryEventDispatchTracksNodeRemoval,DestroyProfileOnBrowserClose,DialMediaRouteProvider,GlobalMediaControls,HttpsUpgrades,LensOverlay,MediaRouter,PaintHolding,ThirdPartyStoragePartitioning,Translate,AutoDeElevate,RenderDocument,OptimizationHints,msForceBrowserSignIn,msEdgeUpdateLaunchServicesPreferredVersion",
                "--hide-scrollbars",
                "--mute-audio",
                "--blink-settings=primaryHoverType=2,availableHoverTypes=2,primaryPointerType=4,availablePointerTypes=4",
                "about:blank"
            };
            if (allowFileAccessFromFiles)
            {
                arguments.Insert(arguments.Count - 1, "--allow-file-access-from-files");
            }
            if (disableSandbox)
            {
                arguments.Insert(1, "--no-sandbox");
            }
            if (ChromiumSandboxPolicy.RequiresLegacyWindowsCompatibilityMode())
            {
                // Chromium's GPU child can fail to load on Windows 10 1809 and
                // surface a misleading VERSION.dll dialog even with --disable-gpu.
                arguments.Insert(1, "--in-process-gpu");
            }
            if (disableDevShmUsage)
            {
                arguments.Insert(1, "--disable-dev-shm-usage");
            }

            return arguments;
        }

        internal static bool IsNavigationAllowed(string url, BrowserNavigationPolicy policy)
        {
            if (policy == BrowserNavigationPolicy.Unrestricted)
            {
                return true;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (policy == BrowserNavigationPolicy.LocalFilesOnly)
            {
                return uri.Scheme is "file" or "data" or "about" or "blob";
            }

            return uri.Scheme == Uri.UriSchemeHttps &&
                   string.Equals(uri.Host, "www.i5a6.com", StringComparison.OrdinalIgnoreCase) &&
                   (uri.IsDefaultPort || uri.Port == 443) &&
                   string.IsNullOrEmpty(uri.UserInfo);
        }

        private async Task ConfigureNavigationPolicyAsync(
            IBrowserContext context,
            CancellationToken cancellationToken)
        {
            if (_navigationPolicy == BrowserNavigationPolicy.Unrestricted)
            {
                return;
            }

            await context.RouteAsync(
                    "**/*",
                    route => IsNavigationAllowed(route.Request.Url, _navigationPolicy)
                        ? route.ContinueAsync()
                        : route.AbortAsync())
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private bool ShouldRecycle()
        {
            if (_browser == null || _remoteBrowser)
            {
                return false;
            }

            int maxUses = ReadPositiveInt(RecycleUsesEnvironmentVariable, 100, 1, 1000);
            int maxMinutes = ReadPositiveInt(RecycleMinutesEnvironmentVariable, 30, 1, 240);
            return Volatile.Read(ref _useCount) >= maxUses ||
                   _timeProvider.GetUtcNow() - _startedAt >= TimeSpan.FromMinutes(maxMinutes);
        }

        private async Task RecycleIdleBrowserAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_stopping || _activeOperations != 0 || _remoteBrowser || _browser == null)
                {
                    return;
                }

                await StopBrowserCoreAsync().ConfigureAwait(false);
                _recycleRequested = false;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private void CleanupStaleProfiles()
        {
            try
            {
                CleanupStaleProcessDirectories(
                    Path.Combine(_pathProvider.CacheRoot, "Br"),
                    "p-*");
                CleanupStaleProcessDirectories(
                    Path.Combine(_pathProvider.CacheRoot, "Ba"),
                    "p-*");
                CleanupStaleProcessDirectories(
                    Path.Combine(_pathProvider.CacheRoot, "BrowserRuntime"),
                    "automation-*");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Stale cache cleanup is best effort and must not prevent browser startup.
            }
        }

        private static void CleanupStaleProcessDirectories(string root, string searchPattern)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string directory in Directory.EnumerateDirectories(root, searchPattern, SearchOption.TopDirectoryOnly))
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
            _idleRecycleScheduler.Cancel();
            IBrowser? browser = _browser;
            _browser = null;
            if (browser != null)
            {
                try
                {
                    await browser.CloseAsync()
                        .WaitAsync(BrowserShutdownTimeout)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // A CDP-connected browser can stop responding during shutdown;
                    // the owned process is force-terminated below so disposal never
                    // leaves a report worker waiting indefinitely.
                }
            }
            Process? process = _process;
            _process = null;
            BrowserProcessRegistration? registration = _registration;
            _registration = null;
            if (process != null)
            {
                await BrowserRuntimeManager.KillOwnedProcessAsync(process).ConfigureAwait(false);
                registration?.Dispose();
                process.Dispose();
            }
            IPlaywright? playwright = _playwright;
            _playwright = null;
            await DisposePlaywrightAsync(playwright).ConfigureAwait(false);
            _remoteBrowser = false;
            await DeleteRuntimeDirectoryAsync(_profileRoot).ConfigureAwait(false);
            await DeleteRuntimeDirectoryAsync(_artifactsRoot).ConfigureAwait(false);
            _profileRoot = null;
            _artifactsRoot = null;
            _useCount = 0;
        }

        private static async Task DisposePlaywrightAsync(IPlaywright? playwright)
        {
            if (playwright == null) return;
            try
            {
                await Task.Run(playwright.Dispose)
                    .WaitAsync(BrowserShutdownTimeout)
                    .ConfigureAwait(false);
            }
            catch
            {
                // The owned process is already gone; a stalled driver pipe must not block shutdown.
            }
        }

        private static async Task DeleteRuntimeDirectoryAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            for (int attempt = 0; attempt < 5 && Directory.Exists(path); attempt++)
            {
                AtomicFileHelper.TryDeleteDirectory(path);
                if (Directory.Exists(path)) await Task.Delay(200).ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_disposeSync)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stopping = true;
            _shutdownSource.Cancel();
            _operationsDrained ??= new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task operationsDrained = _operationsDrained.Task;
            if (await _lifecycleGate.WaitAsync(LifecycleGateTimeout).ConfigureAwait(false))
            {
                try
                {
                    if (_activeOperations == 0)
                    {
                        _operationsDrained.TrySetResult(true);
                    }
                }
                finally
                {
                    _lifecycleGate.Release();
                }
            }

            // Stop the idle worker after marking the host as stopping and
            // before competing with it for browser shutdown. This prevents a
            // delayed recycle from racing explicit host disposal on macOS.
            Task idleSchedulerStopped = _idleRecycleScheduler.DisposeAsync().AsTask();
            await WaitWithinShutdownBudgetAsync(idleSchedulerStopped).ConfigureAwait(false);
            await WaitWithinShutdownBudgetAsync(operationsDrained).ConfigureAwait(false);
            await TryStopBrowserAsync().ConfigureAwait(false);
            await WaitWithinShutdownBudgetAsync(operationsDrained).ConfigureAwait(false);

            if (operationsDrained.IsCompleted && idleSchedulerStopped.IsCompleted)
            {
                _shutdownSource.Dispose();
                _lifecycleGate.Dispose();
            }
        }

        private async Task TryStopBrowserAsync()
        {
            if (!await _lifecycleGate.WaitAsync(LifecycleGateTimeout).ConfigureAwait(false)) return;
            try
            {
                await StopBrowserCoreAsync().ConfigureAwait(false);
                if (_activeOperations == 0)
                {
                    _operationsDrained?.TrySetResult(true);
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private static async Task WaitWithinShutdownBudgetAsync(Task task)
        {
            try
            {
                await task.WaitAsync(BrowserShutdownTimeout).ConfigureAwait(false);
            }
            catch
            {
                // Timeout or late cleanup failure must not hold application shutdown open.
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private static int ReadPositiveInt(string name, int fallback, int minimum, int maximum)
        {
            string value = Environment.GetEnvironmentVariable(name) ?? string.Empty;
            return int.TryParse(value.Trim(), out int parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.BrowserRuntime
{
    public enum BrowserWorkloadKind
    {
        PdfRendering,
        WebAutomation
    }

    public sealed record BrowserRuntimeSnapshot(
        int ActivePdfTasks,
        int ActiveAutomationTasks,
        IReadOnlyList<int> OwnedProcessIds);

    public sealed class BrowserRuntimeManager : IAsyncDisposable, IDisposable
    {
        public const string GlobalConcurrencyEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_GLOBAL_CONCURRENCY";
        public const string PdfConcurrencyEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_PDF_CONCURRENCY";
        public const string AutomationConcurrencyEnvironmentVariable = "EXPORTDOCMANAGER_BROWSER_AUTOMATION_CONCURRENCY";

        private readonly SemaphoreSlim _globalGate;
        private readonly SemaphoreSlim _pdfGate;
        private readonly SemaphoreSlim _automationGate;
        private readonly ConcurrentDictionary<int, OwnedBrowserProcess> _ownedProcesses = new();
        private readonly CancellationTokenSource _shutdownSource = new();
        private readonly object _lifecycleSync = new();
        private int _activePdfTasks;
        private int _activeAutomationTasks;
        private int _activeLeases;
        private int _disposed;
        private TaskCompletionSource<bool> _leasesDrained;
        private Task _disposeTask;

        public BrowserRuntimeManager()
        {
            _globalGate = new SemaphoreSlim(ReadLimit(GlobalConcurrencyEnvironmentVariable, 2, 1, 8));
            _pdfGate = new SemaphoreSlim(ReadLimit(PdfConcurrencyEnvironmentVariable, 2, 1, 8));
            _automationGate = new SemaphoreSlim(ReadLimit(AutomationConcurrencyEnvironmentVariable, 1, 1, 4));
        }

        public async Task<BrowserWorkloadLease> AcquireAsync(
            BrowserWorkloadKind workload,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            SemaphoreSlim workloadGate = workload == BrowserWorkloadKind.PdfRendering ? _pdfGate : _automationGate;
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdownSource.Token);
            // Acquire the specific workload slot first. Holding a global slot while
            // waiting for a saturated workload queue creates head-of-line blocking:
            // a queued automation request could otherwise prevent an available PDF
            // slot from being used. Every acquisition path uses this order and the
            // release path mirrors it.
            try
            {
                await workloadGate.WaitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                _shutdownSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(BrowserRuntimeManager));
            }

            try
            {
                await _globalGate.WaitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                _shutdownSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                workloadGate.Release();
                throw new ObjectDisposedException(nameof(BrowserRuntimeManager));
            }
            catch
            {
                workloadGate.Release();
                throw;
            }

            lock (_lifecycleSync)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    _globalGate.Release();
                    workloadGate.Release();
                    throw new ObjectDisposedException(nameof(BrowserRuntimeManager));
                }

                _activeLeases++;
            }

            if (workload == BrowserWorkloadKind.PdfRendering)
                Interlocked.Increment(ref _activePdfTasks);
            else
                Interlocked.Increment(ref _activeAutomationTasks);

            return new BrowserWorkloadLease(this, workload, workloadGate);
        }

        public BrowserProcessRegistration RegisterOwnedProcess(Process process, BrowserWorkloadKind workload, string purpose)
        {
            ArgumentNullException.ThrowIfNull(process);
            ThrowIfDisposed();
            if (process.Id <= 0) throw new InfrastructureServiceException("浏览器进程尚未启动，无法登记归属。");
            var owned = new OwnedBrowserProcess(process, workload, purpose ?? string.Empty);
            _ownedProcesses[process.Id] = owned;
            return new BrowserProcessRegistration(this, process.Id);
        }

        public BrowserRuntimeSnapshot GetSnapshot() => new(
            Volatile.Read(ref _activePdfTasks),
            Volatile.Read(ref _activeAutomationTasks),
            _ownedProcesses.Keys.OrderBy(id => id).ToList());

        internal void Release(BrowserWorkloadKind workload, SemaphoreSlim workloadGate)
        {
            if (workload == BrowserWorkloadKind.PdfRendering)
                Interlocked.Decrement(ref _activePdfTasks);
            else
                Interlocked.Decrement(ref _activeAutomationTasks);
            workloadGate.Release();
            _globalGate.Release();
            lock (_lifecycleSync)
            {
                _activeLeases = Math.Max(0, _activeLeases - 1);
                if (_activeLeases == 0)
                {
                    _leasesDrained?.TrySetResult(true);
                }
            }
        }

        internal void Unregister(int processId) => _ownedProcesses.TryRemove(processId, out _);

        public ValueTask DisposeAsync()
        {
            lock (_lifecycleSync)
            {
                _disposeTask ??= DisposeCoreAsync();
                return new ValueTask(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _shutdownSource.Cancel();

            Task leasesDrained;
            lock (_lifecycleSync)
            {
                if (_activeLeases == 0)
                {
                    leasesDrained = Task.CompletedTask;
                }
                else
                {
                    _leasesDrained ??= new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    leasesDrained = _leasesDrained.Task;
                }
            }

            await leasesDrained.ConfigureAwait(false);
            foreach (var owned in _ownedProcesses.Values.OrderBy(item => item.Process.Id))
            {
                await KillOwnedProcessAsync(owned.Process).ConfigureAwait(false);
            }
            _ownedProcesses.Clear();
            _globalGate.Dispose();
            _pdfGate.Dispose();
            _automationGate.Dispose();
            _shutdownSource.Dispose();
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        }

        internal static async Task KillOwnedProcessAsync(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch { }
        }

        private static int ReadLimit(string name, int fallback, int minimum, int maximum)
        {
            string value = Environment.GetEnvironmentVariable(name) ?? string.Empty;
            return int.TryParse(value.Trim(), out int parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
        }

        private sealed record OwnedBrowserProcess(Process Process, BrowserWorkloadKind Workload, string Purpose);
    }

    public sealed class BrowserWorkloadLease : IAsyncDisposable
    {
        private BrowserRuntimeManager _owner;
        private readonly BrowserWorkloadKind _workload;
        private readonly SemaphoreSlim _workloadGate;

        internal BrowserWorkloadLease(BrowserRuntimeManager owner, BrowserWorkloadKind workload, SemaphoreSlim workloadGate)
        {
            _owner = owner;
            _workload = workload;
            _workloadGate = workloadGate;
        }

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_workload, _workloadGate);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class BrowserProcessRegistration : IDisposable
    {
        private BrowserRuntimeManager _owner;
        private readonly int _processId;

        internal BrowserProcessRegistration(BrowserRuntimeManager owner, int processId)
        {
            _owner = owner;
            _processId = processId;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Unregister(_processId);
        }
    }
}

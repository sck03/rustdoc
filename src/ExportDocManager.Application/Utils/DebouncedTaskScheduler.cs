using System.Threading;

namespace ExportDocManager.Utils
{
    public sealed class DebouncedTaskScheduler : IDisposable
    {
        private readonly TimeSpan _delay;
        private readonly object _syncRoot = new();
        private CancellationTokenSource _pendingSource = new();
        private bool _disposed;

        public DebouncedTaskScheduler(TimeSpan delay)
        {
            _delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        public Task ScheduleAsync(Func<CancellationToken, Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            CancellationTokenSource previousSource;
            Task scheduledTask;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return Task.CompletedTask;
                }

                previousSource = _pendingSource;
                _pendingSource = new CancellationTokenSource();
                scheduledTask = ExecuteAsync(action, _pendingSource.Token);
            }

            CancelAndDispose(previousSource);
            return scheduledTask;
        }

        public void CancelPending()
        {
            CancellationTokenSource pendingSource;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                pendingSource = _pendingSource;
                _pendingSource = new CancellationTokenSource();
            }

            CancelAndDispose(pendingSource);
        }

        public void Dispose()
        {
            CancellationTokenSource pendingSource;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                pendingSource = _pendingSource;
                _pendingSource = null;
            }

            CancelAndDispose(pendingSource);
        }

        private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_delay, cancellationToken);
                await action(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private static void CancelAndDispose(CancellationTokenSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}

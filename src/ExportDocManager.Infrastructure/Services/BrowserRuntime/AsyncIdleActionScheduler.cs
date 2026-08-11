namespace ExportDocManager.Services.BrowserRuntime;

internal sealed class AsyncIdleActionScheduler : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Func<CancellationToken, Task> _action;
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly HashSet<Task> _tasks = [];
    private CancellationTokenSource? _pendingSource;
    private bool _disposed;

    public AsyncIdleActionScheduler(Func<CancellationToken, Task> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Schedule(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelPendingCore();
            var source = CancellationTokenSource.CreateLinkedTokenSource(_shutdownSource.Token);
            _pendingSource = source;
            Task task = RunAsync(source, delay);
            _tasks.Add(task);
            _ = task.ContinueWith(
                completedTask =>
                {
                    lock (_sync)
                    {
                        _tasks.Remove(completedTask);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            CancelPendingCore();
        }
    }

    private async Task RunAsync(CancellationTokenSource source, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, source.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (!ReferenceEquals(_pendingSource, source) || _disposed)
                {
                    return;
                }

                _pendingSource = null;
            }

            await _action(source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        finally
        {
            source.Dispose();
        }
    }

    private void CancelPendingCore()
    {
        CancellationTokenSource? source = _pendingSource;
        _pendingSource = null;
        source?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdownSource.Cancel();
            CancelPendingCore();
            tasks = [.. _tasks];
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        _shutdownSource.Dispose();
    }
}

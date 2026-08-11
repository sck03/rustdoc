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

        CancellationTokenSource? previousSource;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previousSource = DetachPendingCore();
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

        TryCancel(previousSource);
    }

    public void Cancel()
    {
        CancellationTokenSource? source;
        lock (_sync)
        {
            source = DetachPendingCore();
        }

        TryCancel(source);
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

    private CancellationTokenSource? DetachPendingCore()
    {
        CancellationTokenSource? source = _pendingSource;
        _pendingSource = null;
        return source;
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? pendingSource;
        Task[] tasks;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pendingSource = DetachPendingCore();
            tasks = [.. _tasks];
        }

        _shutdownSource.Cancel();
        TryCancel(pendingSource);
        await Task.WhenAll(tasks).ConfigureAwait(false);
        _shutdownSource.Dispose();
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The delayed task can complete and dispose its source immediately
            // after it is detached from the scheduler.
        }
    }
}

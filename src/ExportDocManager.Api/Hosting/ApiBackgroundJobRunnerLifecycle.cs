using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRunner
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task[] activeTasks;
            lock (_lifecycleSync)
            {
                if (Volatile.Read(ref _stopping) != 0)
                {
                    return;
                }

                Volatile.Write(ref _stopping, 1);
                activeTasks = _activeJobs.Values.Select(completion => completion.Task).ToArray();
            }

            _applicationStopping.Cancel();
            if (activeTasks.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(activeTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Application shutdown timed out while waiting for {ActiveJobCount} background job(s).",
                    _activeJobs.Count);
            }
        }

        private async Task RunTrackedAsync(
            BackgroundJobSnapshot initial,
            CancellationTokenSource cancellationSource,
            Func<IServiceProvider, ApiBackgroundJobExecutionContext, Task<string>> executeAsync,
            UserConcurrencyState userState,
            TaskCompletionSource completion)
        {
            try
            {
                await RunAsync(initial, cancellationSource, executeAsync, userState).ConfigureAwait(false);
            }
            finally
            {
                completion.TrySetResult();
                _activeJobs.TryRemove(initial.JobId, out _);
            }
        }
    }
}

using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRunner
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task shutdownTask;
            bool initiateShutdown = false;
            lock (_lifecycleSync)
            {
                if (_shutdownTask == null)
                {
                    Volatile.Write(ref _stopping, 1);
                    Task[] activeTasks = _activeJobs.Values
                        .Select(completion => completion.Task)
                        .ToArray();
                    _shutdownTask = activeTasks.Length == 0
                        ? Task.CompletedTask
                        : Task.WhenAll(activeTasks);
                    initiateShutdown = true;
                }

                shutdownTask = _shutdownTask ?? Task.CompletedTask;
            }

            if (initiateShutdown)
            {
                _applicationStopping.Cancel();
            }

            try
            {
                await shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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

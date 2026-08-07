using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Data;

internal static class ExcelAnalysisExecutionGate
{
    internal const string ConcurrencyEnvironmentVariable = "EXPORTDOCMANAGER_EXCEL_ANALYSIS_CONCURRENCY";
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim Gate = new(ResolveMaximumConcurrency());

    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        using var queueTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        queueTimeout.CancelAfter(QueueTimeout);
        try
        {
            await Gate.WaitAsync(queueTimeout.Token).ConfigureAwait(false);
            return new Releaser();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceBusyException("Excel 分析任务较多，请稍后重试。");
        }
    }

    private static int ResolveMaximumConcurrency()
    {
        string configured = Environment.GetEnvironmentVariable(ConcurrencyEnvironmentVariable);
        if (int.TryParse(configured, out int value))
        {
            return Math.Clamp(value, 1, 8);
        }

        return Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    }

    private sealed class Releaser : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Gate.Release();
            }
        }
    }
}

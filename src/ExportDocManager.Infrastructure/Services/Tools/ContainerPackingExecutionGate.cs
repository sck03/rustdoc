using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Tools;

internal static class ContainerPackingExecutionGate
{
    internal const string ConcurrencyEnvironmentVariable =
        "EXPORTDOCMANAGER_CONTAINER_PACKING_CONCURRENCY";
    internal const string TimeoutEnvironmentVariable =
        "EXPORTDOCMANAGER_CONTAINER_PACKING_TIMEOUT_SECONDS";

    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim Gate = new(ResolveMaximumConcurrency());

    public static IDisposable Enter(CancellationToken cancellationToken)
    {
        bool entered = Gate.Wait(QueueTimeout, cancellationToken);
        if (!entered)
        {
            throw new ServiceBusyException("装箱分析任务较多，请稍后重试。");
        }

        return new Lease();
    }

    public static async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        bool entered = await Gate.WaitAsync(QueueTimeout, cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            throw new ServiceBusyException("装箱分析任务较多，请稍后重试。");
        }

        return new Lease();
    }

    public static TimeSpan ResolveOperationTimeout() => TimeSpan.FromSeconds(
        ReadBoundedInt(TimeoutEnvironmentVariable, defaultValue: 10, minimum: 3, maximum: 60));

    private static int ResolveMaximumConcurrency() =>
        ReadBoundedInt(ConcurrencyEnvironmentVariable, defaultValue: 2, minimum: 1, maximum: 8);

    private static int ReadBoundedInt(
        string environmentVariable,
        int defaultValue,
        int minimum,
        int maximum)
    {
        string configured = Environment.GetEnvironmentVariable(environmentVariable);
        return int.TryParse(configured, out int parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : defaultValue;
    }

    private sealed class Lease : IDisposable
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

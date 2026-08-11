using ExportDocManager.Services.BrowserRuntime;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class AsyncIdleActionSchedulerTests
{
    [Fact]
    public async Task DisposeAsync_ShouldCancelPendingDelayPromptly()
    {
        await using var scheduler = new AsyncIdleActionScheduler(_ => Task.CompletedTask);
        scheduler.Schedule(TimeSpan.FromHours(1));

        await scheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Schedule_ShouldReplaceEarlierPendingAction()
    {
        int executionCount = 0;
        await using var scheduler = new AsyncIdleActionScheduler(_ =>
        {
            Interlocked.Increment(ref executionCount);
            return Task.CompletedTask;
        });

        scheduler.Schedule(TimeSpan.FromHours(1));
        scheduler.Schedule(TimeSpan.FromMilliseconds(20));

        await WaitUntilAsync(() => Volatile.Read(ref executionCount) == 1, TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref executionCount));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The scheduled idle action did not complete within the expected time.");
    }
}

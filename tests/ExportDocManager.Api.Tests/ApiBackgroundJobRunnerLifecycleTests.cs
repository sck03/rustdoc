using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Api.Tests;

public sealed class ApiBackgroundJobRunnerLifecycleTests
{
    [Fact]
    public async Task StopAsync_ShouldCancelAndDrainActiveJobs()
    {
        var jobService = new ApiBackgroundJobService();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runner = new ApiBackgroundJobRunner(
            jobService,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApiBackgroundJobRunner>.Instance);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = runner.Enqueue(
            "Test",
            "停机取消任务",
            string.Empty,
            async (_, context) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                return string.Empty;
            });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runner.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        var final = await jobService.GetAsync(job.JobId);
        Assert.NotNull(final);
        Assert.Equal(BackgroundJobStatusCatalog.Canceled, final.Status);
        Assert.False(final.CanCancel);
    }

    [Fact]
    public async Task Enqueue_ShouldRejectNewJobsAfterShutdownBegins()
    {
        var jobService = new ApiBackgroundJobService();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runner = new ApiBackgroundJobRunner(
            jobService,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApiBackgroundJobRunner>.Instance);

        await runner.StopAsync(CancellationToken.None);
        var rejected = runner.Enqueue(
            "Test",
            "停机后任务",
            string.Empty,
            (_, _) => Task.FromResult(string.Empty));

        Assert.Equal(BackgroundJobStatusCatalog.Failed, rejected.Status);
        Assert.Equal(ApiBackgroundJobQueueStatusCatalog.Rejected, rejected.StatusText);
        Assert.Contains("正在停止", rejected.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentEnqueueAndStop_ShouldLeaveEveryJobTerminal()
    {
        var jobService = new ApiBackgroundJobService();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runner = new ApiBackgroundJobRunner(
            jobService,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApiBackgroundJobRunner>.Instance);
        using var start = new ManualResetEventSlim();

        Task<BackgroundJobSnapshot>[] enqueueTasks = Enumerable.Range(0, 64)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                return runner.Enqueue(
                    "Test",
                    $"并发停机任务 {index}",
                    string.Empty,
                    async (_, context) =>
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                        return string.Empty;
                    });
            }))
            .ToArray();

        start.Set();
        Task stopTask = runner.StopAsync(CancellationToken.None);
        BackgroundJobSnapshot[] jobs = await Task.WhenAll(enqueueTasks);
        await stopTask.WaitAsync(TimeSpan.FromSeconds(10));

        foreach (BackgroundJobSnapshot job in jobs)
        {
            BackgroundJobSnapshot final = await jobService.GetAsync(job.JobId);
            Assert.NotNull(final);
            Assert.True(BackgroundJobStatusCatalog.IsTerminal(final.Status));
        }
    }
}

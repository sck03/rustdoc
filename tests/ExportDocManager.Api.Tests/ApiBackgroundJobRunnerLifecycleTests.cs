using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
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
    public async Task StopAsync_AfterCallerTimeout_ShouldAllowLaterCallerToAwaitSameDrain()
    {
        var jobService = new ApiBackgroundJobService();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runner = new ApiBackgroundJobRunner(
            jobService,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApiBackgroundJobRunner>.Instance);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        runner.Enqueue(
            "Test",
            "重复等待停机任务",
            string.Empty,
            async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return string.Empty;
            });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var firstWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await runner.StopAsync(firstWait.Token);
        Task secondWait = runner.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.False(secondWait.IsCompleted);

        release.TrySetResult();
        await secondWait.WaitAsync(TimeSpan.FromSeconds(5));
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

    [Fact]
    public async Task Enqueue_WhenPersistenceFails_ShouldReleaseQueueCapacityAndRemovePhantomJob()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "edm-job-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pathProvider = new RuntimeAppPathProvider(
                Path.Combine(root, "app"),
                Path.Combine(root, "data"));
            Directory.CreateDirectory(pathProvider.CacheRoot);
            string backgroundJobRoot = Path.Combine(pathProvider.CacheRoot, "BackgroundJobs");
            await File.WriteAllTextAsync(backgroundJobRoot, "blocks the persistence directory");

            var jobService = new ApiBackgroundJobService(pathProvider);
            using var provider = new ServiceCollection().BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobService,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance,
                new ApiBackgroundJobConcurrencyOptions
                {
                    GlobalLimit = 1,
                    PerUserLimit = 1,
                    BrowserLimit = 1,
                    GlobalQueueLimit = 4,
                    PerUserQueueLimit = 2
                });

            for (int index = 0; index < 4; index++)
            {
                string outputPath = Path.Combine(
                    pathProvider.ExportRoot,
                    "Browser",
                    "PersistenceFailure",
                    index.ToString(),
                    "partial.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, "partial");

                Assert.ThrowsAny<IOException>(() => runner.Enqueue(
                    "PersistenceFailure",
                    $"持久化失败任务 {index}",
                    "alice",
                    (_, _) => Task.FromResult(string.Empty),
                    initialOutputPath: outputPath));
                Assert.False(File.Exists(outputPath));
            }

            PagedResult<BackgroundJobSnapshot> failedPage = await jobService.QueryAsync(
                new BackgroundJobQuery { PageNumber = 1, PageSize = 100 });
            Assert.Equal(0, failedPage.TotalCount);

            File.Delete(backgroundJobRoot);
            Directory.CreateDirectory(backgroundJobRoot);
            BackgroundJobSnapshot accepted = runner.Enqueue(
                "PersistenceRecovery",
                "持久化恢复任务",
                "alice",
                (_, _) => Task.FromResult(string.Empty));

            Assert.NotEqual(ApiBackgroundJobQueueStatusCatalog.Rejected, accepted.StatusText);
            await runner.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

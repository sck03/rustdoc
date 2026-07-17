using ExportDocManager.Utils;

namespace ExportDocManager.Application.Tests
{
    public class AsyncCoordinationHelperTests
    {
        [Fact]
        public void LatestRequestCoordinator_Begin_ShouldCancelPreviousRequestAndKeepLatestCurrent()
        {
            using var coordinator = new LatestRequestCoordinator();

            var first = coordinator.Begin();
            var second = coordinator.Begin();

            Assert.True(first.CancellationToken.IsCancellationRequested);
            Assert.False(coordinator.IsCurrent(first));
            Assert.False(second.CancellationToken.IsCancellationRequested);
            Assert.True(coordinator.IsCurrent(second));
            Assert.True(second.Version > first.Version);
        }

        [Fact]
        public void LatestRequestCoordinator_CancelCurrent_ShouldCancelCurrentHandle()
        {
            using var coordinator = new LatestRequestCoordinator();
            var handle = coordinator.Begin();

            coordinator.CancelCurrent();

            Assert.True(handle.CancellationToken.IsCancellationRequested);
            Assert.False(coordinator.IsCurrent(handle));
        }

        [Fact]
        public void LatestRequestCoordinator_Dispose_ShouldCancelCurrentRequest()
        {
            var coordinator = new LatestRequestCoordinator();
            var handle = coordinator.Begin();

            coordinator.Dispose();

            Assert.True(handle.CancellationToken.IsCancellationRequested);
            Assert.False(coordinator.IsCurrent(handle));
        }

        [Fact]
        public async Task DebouncedTaskScheduler_ScheduleAsync_ShouldRunLatestActionOnly()
        {
            using var scheduler = new DebouncedTaskScheduler(TimeSpan.FromMilliseconds(20));
            int firstRunCount = 0;
            int secondRunCount = 0;
            var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task firstTask = scheduler.ScheduleAsync(_ =>
            {
                Interlocked.Increment(ref firstRunCount);
                return Task.CompletedTask;
            });
            Task secondTask = scheduler.ScheduleAsync(_ =>
            {
                Interlocked.Increment(ref secondRunCount);
                secondCompleted.TrySetResult();
                return Task.CompletedTask;
            });

            await WaitAsync(secondCompleted.Task);
            await Task.WhenAll(firstTask, secondTask);

            Assert.Equal(0, firstRunCount);
            Assert.Equal(1, secondRunCount);
        }

        [Fact]
        public async Task DebouncedTaskScheduler_CancelPending_ShouldPreventScheduledAction()
        {
            using var scheduler = new DebouncedTaskScheduler(TimeSpan.FromMilliseconds(50));
            int runCount = 0;

            Task scheduledTask = scheduler.ScheduleAsync(_ =>
            {
                Interlocked.Increment(ref runCount);
                return Task.CompletedTask;
            });

            scheduler.CancelPending();
            await scheduledTask;
            await Task.Delay(80);

            Assert.Equal(0, runCount);
        }

        [Fact]
        public async Task DebouncedTaskScheduler_Dispose_ShouldCancelPendingAndIgnoreLaterSchedules()
        {
            var scheduler = new DebouncedTaskScheduler(TimeSpan.FromMilliseconds(50));
            int runCount = 0;

            Task scheduledTask = scheduler.ScheduleAsync(_ =>
            {
                Interlocked.Increment(ref runCount);
                return Task.CompletedTask;
            });

            scheduler.Dispose();
            Task ignoredTask = scheduler.ScheduleAsync(_ =>
            {
                Interlocked.Increment(ref runCount);
                return Task.CompletedTask;
            });

            await Task.WhenAll(scheduledTask, ignoredTask);
            await Task.Delay(80);

            Assert.Equal(0, runCount);
        }

        private static async Task WaitAsync(Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(task, completed);
            await task;
        }
    }
}

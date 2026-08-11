using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiIntegrationTestHarnessTests
    {
        [Fact]
        public async Task StartupFailure_ShouldReleaseSqliteLeaseBeforeCleaningTemporaryRoots()
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ApiIntegrationTestHarness.StartAsync(
                    "startup-failure-cleanup",
                    "startup-failure.db",
                    configureServices: services => services.AddSingleton<IHostedService, FailingHostedService>()));

            Assert.Equal(FailingHostedService.FailureMessage, exception.Message);
        }

        private sealed class FailingHostedService : IHostedService
        {
            public const string FailureMessage = "forced hosted service startup failure";

            public Task StartAsync(CancellationToken cancellationToken) =>
                Task.FromException(new InvalidOperationException(FailureMessage));

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}

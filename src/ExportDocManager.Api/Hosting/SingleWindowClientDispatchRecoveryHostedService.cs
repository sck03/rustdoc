using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting;

/// <summary>
/// Reconciles client-dispatch leases left behind by an interrupted desktop request.
/// The bridge remains the single owner of state transitions; this service only supplies
/// a bounded, cancellable scheduler and a scoped dependency lifetime.
/// </summary>
public sealed class SingleWindowClientDispatchRecoveryHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SingleWindowClientDispatchRecoveryHostedService> _logger;

    public SingleWindowClientDispatchRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SingleWindowClientDispatchRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var bridge = scope.ServiceProvider.GetRequiredService<ISingleWindowClientBridge>();
            int recovered = await bridge
                .RecoverExpiredDispatchesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (recovered > 0)
            {
                _logger.LogInformation(
                    "Recovered {Count} expired Single Window client dispatch lease(s).",
                    recovered);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A transient database/file issue must not terminate the whole API host;
            // the next tick retries the bounded reconciliation.
            _logger.LogError(ex, "Single Window client dispatch recovery check failed.");
        }
    }
}

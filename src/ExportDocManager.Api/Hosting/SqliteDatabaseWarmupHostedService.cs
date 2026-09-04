using ExportDocManager.Services.Infrastructure;
namespace ExportDocManager.Api.Hosting;

public sealed class SqliteDatabaseWarmupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqliteDatabaseWarmupHostedService> _logger;
    public SqliteDatabaseWarmupHostedService(IServiceScopeFactory scopeFactory, ILogger<SqliteDatabaseWarmupHostedService> logger) =>
        (_scopeFactory, _logger) = (scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory)), logger ?? throw new ArgumentNullException(nameof(logger)));
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IDatabaseInitializationService>()
                .InitializeAsync("admin", string.Empty, cancellationToken: stoppingToken).ConfigureAwait(false);
            if (result.IsSuccess) _logger.LogDebug("SQLite database baseline is ready before the first login.");
            else _logger.LogInformation("SQLite background warmup deferred: {Message}", result.ErrorMessage);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "SQLite background warmup could not complete; login will retry it.");
        }
    }
}

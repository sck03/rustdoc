using ExportDocManager.DataAccess;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Hosting;

public sealed record ApiReadinessSnapshot(
    bool Ready,
    DateTimeOffset CheckedAt,
    IReadOnlyDictionary<string, string> Checks);

public interface IApiReadinessProbe
{
    Task<ApiReadinessSnapshot> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs bounded checks for dependencies required to accept new work. The
/// response deliberately contains only stable check names and states, never
/// connection strings, server paths, or raw infrastructure exception messages.
/// </summary>
public sealed class ApiReadinessProbe : IApiReadinessProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAppPathProvider _pathProvider;

    public ApiReadinessProbe(
        IDbContextFactory<AppDbContext> contextFactory,
        IAppPathProvider pathProvider)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<ApiReadinessSnapshot> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        Task<bool> databaseCheck = CanConnectDatabaseAsync(timeout.Token);
        Task<bool> browserCheck = CanReachConfiguredBrowserAsync(timeout.Token);
        bool runtimeDirectoriesReady = RequiredRuntimeDirectoriesExist();

        bool databaseReady;
        bool browserReady;
        try
        {
            await Task.WhenAll(databaseCheck, browserCheck).ConfigureAwait(false);
            databaseReady = databaseCheck.Result;
            browserReady = browserCheck.Result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            databaseReady = databaseCheck.IsCompletedSuccessfully && databaseCheck.Result;
            browserReady = browserCheck.IsCompletedSuccessfully && browserCheck.Result;
        }

        var checks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["database"] = databaseReady ? "ready" : "unavailable",
            ["runtimeDirectories"] = runtimeDirectoriesReady ? "ready" : "unavailable",
            ["browser"] = browserReady ? "ready" : "unavailable"
        };
        return new ApiReadinessSnapshot(
            databaseReady && runtimeDirectoriesReady && browserReady,
            DateTimeOffset.UtcNow,
            checks);
    }

    private async Task<bool> CanConnectDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AppDbContext context =
                await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> CanReachConfiguredBrowserAsync(
        CancellationToken cancellationToken)
    {
        if (!BrowserCdpEndpointPolicy.TryResolve(out Uri? endpoint))
        {
            // Desktop and non-container server packages use an on-demand local
            // renderer. Starting a browser from a health probe would consume
            // substantial resources, so only configured remote CDP is probed.
            return true;
        }

        try
        {
            await BrowserCdpConnectionResolver
                .ResolveWebSocketEndpointAsync(endpoint, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool RequiredRuntimeDirectoriesExist() =>
        Directory.Exists(_pathProvider.DataRoot) &&
        Directory.Exists(_pathProvider.ConfigRoot) &&
        Directory.Exists(_pathProvider.CacheRoot) &&
        Directory.Exists(_pathProvider.LogRoot) &&
        Directory.Exists(_pathProvider.SecurityRoot);
}

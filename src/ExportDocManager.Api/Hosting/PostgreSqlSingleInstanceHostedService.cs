using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using Npgsql;

namespace ExportDocManager.Api.Hosting;

/// <summary>
/// The current background-job and maintenance orchestration model intentionally
/// supports one API process per PostgreSQL business database. A session advisory
/// lock makes that deployment boundary explicit instead of allowing two processes
/// to execute the same local task catalogue concurrently.
/// </summary>
public sealed class PostgreSqlSingleInstanceHostedService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(15);
    private readonly DatabaseConnectionSettings _databaseSettings;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<PostgreSqlSingleInstanceHostedService> _logger;
    private readonly Lock _gate = new();
    private NpgsqlConnection _connection;
    private CancellationTokenSource _monitorCancellation;
    private Task _monitorTask = Task.CompletedTask;
    private long _lockId;

    public PostgreSqlSingleInstanceHostedService(
        DatabaseConnectionSettings databaseSettings,
        IHostApplicationLifetime applicationLifetime,
        ILogger<PostgreSqlSingleInstanceHostedService> logger)
    {
        _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!DatabaseModeHelper.UsesPostgreSql(_databaseSettings))
        {
            return;
        }

        _lockId = CalculateLockId(_databaseSettings.PostgreSqlDatabase);
        var connection = new NpgsqlConnection(DbHelper.BuildPostgreSqlConnectionString(_databaseSettings));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock($1);";
            command.Parameters.AddWithValue(_lockId);
            command.CommandTimeout = 10;
            bool acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
            if (!acquired)
            {
                throw new InvalidOperationException(
                    "当前 PostgreSQL 业务库已有 ExportDocManager API 实例运行。请停止重复实例后重试。");
            }

            lock (_gate)
            {
                _connection = connection;
                _monitorCancellation = new CancellationTokenSource();
                _monitorTask = MonitorConnectionAsync(connection, _monitorCancellation.Token);
            }
            connection = null;
        }
        finally
        {
            if (connection != null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource monitorCancellation;
        Task monitorTask;
        NpgsqlConnection connection;
        lock (_gate)
        {
            monitorCancellation = _monitorCancellation;
            monitorTask = _monitorTask;
            connection = _connection;
            _monitorCancellation = null;
            _monitorTask = Task.CompletedTask;
            _connection = null;
        }

        monitorCancellation?.Cancel();
        try
        {
            await monitorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (monitorCancellation?.IsCancellationRequested == true)
        {
        }
        finally
        {
            monitorCancellation?.Dispose();
        }

        if (connection == null)
        {
            return;
        }

        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock($1);";
                command.Parameters.AddWithValue(_lockId);
                command.CommandTimeout = 5;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to explicitly release the PostgreSQL single-instance advisory lock.");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    internal static long CalculateLockId(string databaseName)
    {
        string identity = "ExportDocManager.Api:" + (databaseName ?? string.Empty).Trim().ToUpperInvariant();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return BinaryPrimitives.ReadInt64LittleEndian(digest);
    }

    private async Task MonitorConnectionAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(HealthInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.CommandTimeout = 5;
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "The PostgreSQL single-instance lease connection was lost; stopping the API to prevent overlapping task execution.");
            _applicationLifetime.StopApplication();
        }
    }
}

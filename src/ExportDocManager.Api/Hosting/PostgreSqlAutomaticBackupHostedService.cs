using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed class PostgreSqlAutomaticBackupHostedService : BackgroundService
    {
        private const string StateFileName = "schedule-state.json";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAppPathProvider _pathProvider;
        private readonly ILogger<PostgreSqlAutomaticBackupHostedService> _logger;

        public PostgreSqlAutomaticBackupHostedService(
            IServiceScopeFactory scopeFactory,
            IAppPathProvider pathProvider,
            ILogger<PostgreSqlAutomaticBackupHostedService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RunCheckAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCheckAsync(stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task RunCheckAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                await settingsService.LoadAsync().ConfigureAwait(false);
                var settings = settingsService.Settings?.System ?? new SystemSettings();
                if (!settings.PostgreSqlAutoBackupEnabled)
                {
                    return;
                }

                var now = DateTimeOffset.Now;
                if (!ShouldRun(settings, now, await ReadStateAsync(cancellationToken).ConfigureAwait(false)))
                {
                    return;
                }

                var maintenance = scope.ServiceProvider.GetRequiredService<ISharedDatabaseMaintenanceService>();
                var status = maintenance.GetPostgreSqlMaintenanceStatus();
                if (!status.PostgreSqlConfigured || !status.ToolsReady)
                {
                    _logger.LogWarning(
                        "PostgreSQL automatic backup skipped. configured={Configured}, toolsReady={ToolsReady}.",
                        status.PostgreSqlConfigured,
                        status.ToolsReady);
                    return;
                }

                var result = await maintenance.CreatePostgreSqlPhysicalBackupAsync(cancellationToken).ConfigureAwait(false);
                await PruneBackupsAsync(settings.PostgreSqlAutoBackupRetentionCount, cancellationToken).ConfigureAwait(false);
                await WriteStateAsync(new PostgreSqlAutomaticBackupState
                {
                    LastSuccessfulRunAt = now,
                    LastBackupFileName = result.FileName
                }, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "PostgreSQL automatic backup created: {FileName}, size={SizeBytes}.",
                    result.FileName,
                    result.SizeBytes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgreSQL automatic backup check failed.");
            }
        }

        private static bool ShouldRun(
            SystemSettings settings,
            DateTimeOffset now,
            PostgreSqlAutomaticBackupState state)
        {
            if (!TimeSpan.TryParse(settings.PostgreSqlAutoBackupTime, out var scheduledTime))
            {
                scheduledTime = new TimeSpan(2, 0, 0);
            }

            if (now.TimeOfDay < scheduledTime)
            {
                return false;
            }

            bool weekly = string.Equals(settings.PostgreSqlAutoBackupSchedule, "Weekly", StringComparison.OrdinalIgnoreCase);
            if (weekly && (int)now.DayOfWeek != Math.Clamp(settings.PostgreSqlAutoBackupDayOfWeek, 0, 6))
            {
                return false;
            }

            var currentPeriod = GetPeriodKey(now, weekly);
            var lastPeriod = state?.LastSuccessfulRunAt == null
                ? string.Empty
                : GetPeriodKey(state.LastSuccessfulRunAt.Value, weekly);

            return !string.Equals(currentPeriod, lastPeriod, StringComparison.Ordinal);
        }

        private static string GetPeriodKey(DateTimeOffset value, bool weekly)
        {
            if (!weekly)
            {
                return value.ToString("yyyy-MM-dd");
            }

            var date = value.DateTime;
            return $"{System.Globalization.ISOWeek.GetYear(date):0000}-W{System.Globalization.ISOWeek.GetWeekOfYear(date):00}";
        }

        private Task PruneBackupsAsync(int retentionCount, CancellationToken cancellationToken)
        {
            if (retentionCount <= 0)
            {
                return Task.CompletedTask;
            }

            string backupRoot = PostgreSqlBackupRoot;
            if (!Directory.Exists(backupRoot))
            {
                return Task.CompletedTask;
            }

            var oldFiles = new DirectoryInfo(backupRoot)
                .EnumerateFiles("*.dump", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(retentionCount)
                .ToArray();
            foreach (var file in oldFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    file.Delete();
                    _logger.LogInformation("Pruned old PostgreSQL backup: {FileName}.", file.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Failed to prune old PostgreSQL backup: {FileName}.", file.FullName);
                }
            }

            return Task.CompletedTask;
        }

        private async Task<PostgreSqlAutomaticBackupState> ReadStateAsync(CancellationToken cancellationToken)
        {
            string path = StatePath;
            if (!File.Exists(path))
            {
                return new PostgreSqlAutomaticBackupState();
            }

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return await JsonSerializer.DeserializeAsync<PostgreSqlAutomaticBackupState>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                    ?? new PostgreSqlAutomaticBackupState();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Failed to read PostgreSQL automatic backup state.");
                return new PostgreSqlAutomaticBackupState();
            }
        }

        private async Task WriteStateAsync(PostgreSqlAutomaticBackupState state, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(PostgreSqlBackupRoot);
            await File.WriteAllTextAsync(
                StatePath,
                JsonSerializer.Serialize(state, JsonOptions),
                cancellationToken).ConfigureAwait(false);
        }

        private string PostgreSqlBackupRoot =>
            Path.Combine(_pathProvider.BackupRoot, "PostgreSQL");

        private string StatePath =>
            Path.Combine(PostgreSqlBackupRoot, StateFileName);

        private sealed class PostgreSqlAutomaticBackupState
        {
            public DateTimeOffset? LastSuccessfulRunAt { get; set; }
            public string LastBackupFileName { get; set; } = string.Empty;
        }
    }
}

using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class SystemLogCleanupService : ISystemLogCleanupService
    {
        private const int AuditLogCleanupMaxCount = 300000;

        private readonly ISettingsService _settingsService;
        private readonly IAuditLogService _auditLogService;
        private readonly IAppPathProvider _pathProvider;

        public SystemLogCleanupService(
            ISettingsService settingsService,
            IAuditLogService auditLogService,
            IAppPathProvider pathProvider)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public async Task<SystemLogCleanupResult> CleanAsync(CancellationToken cancellationToken = default)
        {
            await _settingsService.LoadAsync().ConfigureAwait(false);
            var systemSettings = _settingsService.Settings?.System ?? new SystemSettings();

            int deletedAuditLogs = 0;
            if (systemSettings.AuditLogRetentionDays > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cutoffUtc = DateTime.UtcNow.AddDays(-systemSettings.AuditLogRetentionDays);
                deletedAuditLogs = await _auditLogService
                    .DeleteOlderThanAsync(cutoffUtc, AuditLogCleanupMaxCount)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            int retainedFileCount = Math.Max(1, systemSettings.LogRetainedFileCount);
            var textLogSummary = TextLogCleanupHelper.Clean(
                _pathProvider.LogRoot,
                systemSettings.LogRetentionDays,
                retainedFileCount);

            return new SystemLogCleanupResult
            {
                DeletedAuditLogs = deletedAuditLogs,
                DeletedTextLogsByAge = textLogSummary.DeletedByAge,
                DeletedTextLogsByCount = textLogSummary.DeletedByCount,
                DeletedTextLogs = textLogSummary.TotalDeleted
            };
        }
    }
}

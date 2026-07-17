using ExportDocManager.Models.DTOs;
using ExportDocManager.Utils;
using Serilog;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class ShutdownMaintenanceService : IShutdownMaintenanceService
    {
        private const int AuditLogCleanupMaxCount = 200000;

        private readonly ISettingsService _settingsService;
        private readonly IBackupService _backupService;
        private readonly ICloudSyncService _cloudSyncService;
        private readonly IAuditLogService _auditLogService;
        private readonly IAppPathProvider _pathProvider;

        public ShutdownMaintenanceService(
            ISettingsService settingsService,
            IBackupService backupService,
            ICloudSyncService cloudSyncService,
            IAuditLogService auditLogService,
            IAppPathProvider pathProvider)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _cloudSyncService = cloudSyncService ?? throw new ArgumentNullException(nameof(cloudSyncService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public async Task<ShutdownMaintenanceResult> RunAsync(CancellationToken cancellationToken = default)
        {
            await _settingsService.LoadAsync().ConfigureAwait(false);
            var systemSettings = _settingsService.Settings?.System;
            if (systemSettings == null)
            {
                return new ShutdownMaintenanceResult();
            }

            string uploadedBackupFileName = string.Empty;
            string cloudSyncErrorMessage = string.Empty;
            int deletedAuditLogs = 0;
            int deletedTextLogs = 0;

            if (systemSettings.BackupRetentionDays > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _backupService.CleanOldBackups(systemSettings.BackupRetentionDays);
                await _backupService.BackupDatabaseAsync().ConfigureAwait(false);

                try
                {
                    uploadedBackupFileName = await UploadLatestBackupAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    cloudSyncErrorMessage = ex.Message;
                    Log.Warning(ex, "Cloud backup upload failed during shutdown maintenance.");
                }
            }

            if (systemSettings.AuditLogRetentionDays > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cutoffUtc = DateTime.UtcNow.AddDays(-systemSettings.AuditLogRetentionDays);
                deletedAuditLogs = await _auditLogService
                    .DeleteOlderThanAsync(cutoffUtc, AuditLogCleanupMaxCount)
                    .ConfigureAwait(false);
            }

            if (systemSettings.LogRetentionDays > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                deletedTextLogs = TextLogCleanupHelper
                    .Clean(_pathProvider.LogRoot, systemSettings.LogRetentionDays, retainedFileCount: 0)
                    .TotalDeleted;
            }

            return new ShutdownMaintenanceResult
            {
                DeletedAuditLogs = deletedAuditLogs,
                DeletedTextLogs = deletedTextLogs,
                UploadedBackupFileName = uploadedBackupFileName,
                CloudSyncErrorMessage = cloudSyncErrorMessage
            };
        }

        private async Task<string> UploadLatestBackupAsync(CancellationToken cancellationToken)
        {
            if (_settingsService.Settings?.WebDav?.Enabled != true)
            {
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var backups = _backupService.GetAvailableBackups();
            if (backups == null || backups.Count == 0)
            {
                return string.Empty;
            }

            var latestBackup = backups[0];
            var fileName = Path.GetFileName(latestBackup);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            await _cloudSyncService.UploadFileAsync(latestBackup, fileName).ConfigureAwait(false);
            return fileName;
        }
    }
}

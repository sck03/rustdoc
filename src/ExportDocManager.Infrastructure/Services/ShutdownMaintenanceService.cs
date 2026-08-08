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
            var maintenanceErrors = new List<string>();
            int deletedAuditLogs = 0;
            int deletedTextLogs = 0;

            if (systemSettings.BackupRetentionDays > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _backupService.CleanOldBackups(systemSettings.BackupRetentionDays);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AddMaintenanceError(maintenanceErrors, ex.Message);
                    Log.Warning(ex, "Old database backup cleanup failed during shutdown maintenance.");
                }

                var backupResult = await _backupService
                    .BackupDatabaseAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!backupResult.Success && !backupResult.Skipped)
                {
                    AddMaintenanceError(maintenanceErrors, backupResult.Message);
                    Log.Warning("Local database backup failed during shutdown maintenance: {Message}", backupResult.Message);
                }
                else if (backupResult.Success)
                {
                    try
                    {
                        uploadedBackupFileName = await UploadBackupAsync(
                            backupResult.FilePath,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        AddMaintenanceError(maintenanceErrors, ex.Message);
                        Log.Warning(ex, "Cloud backup upload failed during shutdown maintenance.");
                    }
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
                    .Clean(
                        _pathProvider.LogRoot,
                        systemSettings.LogRetentionDays,
                        retainedFileCount: 0,
                        maxFileSizeMB: systemSettings.LogFileSizeLimitMB)
                    .TotalDeleted;
            }

            return new ShutdownMaintenanceResult
            {
                DeletedAuditLogs = deletedAuditLogs,
                DeletedTextLogs = deletedTextLogs,
                UploadedBackupFileName = uploadedBackupFileName,
                CloudSyncErrorMessage = string.Join("；", maintenanceErrors)
            };
        }

        private async Task<string> UploadBackupAsync(string backupFilePath, CancellationToken cancellationToken)
        {
            if (_settingsService.Settings?.WebDav?.Enabled != true)
            {
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string resolvedBackupPath;
            try
            {
                resolvedBackupPath = Path.GetFullPath(backupFilePath ?? string.Empty);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidDataException("新创建的数据库备份路径无效，已停止云端上传。", ex);
            }

            if (!PathBoundaryHelper.IsWithinRoot(resolvedBackupPath, _pathProvider.BackupRoot) ||
                !string.Equals(Path.GetExtension(resolvedBackupPath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("新创建的数据库备份不在运行数据根 Backups 目录内或不是 ZIP 文件，已停止云端上传。");
            }

            if (!File.Exists(resolvedBackupPath))
            {
                throw new FileNotFoundException("新创建的数据库备份文件不存在，已停止云端上传。", resolvedBackupPath);
            }

            var fileName = Path.GetFileName(resolvedBackupPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidDataException("新创建的数据库备份文件名无效，已停止云端上传。");
            }

            await _cloudSyncService.UploadFileAsync(resolvedBackupPath, fileName, cancellationToken).ConfigureAwait(false);
            return fileName;
        }

        private static void AddMaintenanceError(ICollection<string> errors, string message)
        {
            string normalized = (message ?? string.Empty).Trim();
            if (normalized.Length > 0 && !errors.Contains(normalized, StringComparer.Ordinal))
            {
                errors.Add(normalized);
            }
        }
    }
}

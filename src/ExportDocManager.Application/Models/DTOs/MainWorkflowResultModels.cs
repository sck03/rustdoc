namespace ExportDocManager.Models.DTOs
{
    public sealed class ShutdownMaintenanceResult
    {
        public int DeletedAuditLogs { get; init; }

        public int DeletedTextLogs { get; init; }

        public string UploadedBackupFileName { get; init; } = string.Empty;

        public string CloudSyncErrorMessage { get; init; } = string.Empty;

        public bool CloudSyncFailed => !string.IsNullOrWhiteSpace(CloudSyncErrorMessage);
    }

    public sealed class SystemLogCleanupResult
    {
        public int DeletedAuditLogs { get; init; }

        public int DeletedTextLogsByAge { get; init; }

        public int DeletedTextLogsByCount { get; init; }

        public int DeletedTextLogs { get; init; }
    }
}

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiShutdownMaintenanceResponse(
        bool Success,
        string Message,
        int DeletedAuditLogs,
        int DeletedTextLogs,
        string UploadedBackupFileName,
        string CloudSyncErrorMessage,
        bool CloudSyncFailed,
        string BackupRoot,
        string LogRoot,
        string StoragePolicy);

    public sealed record ApiSystemLogCleanupResponse(
        bool Success,
        string Message,
        int DeletedAuditLogs,
        int DeletedTextLogs,
        int DeletedTextLogsByAge,
        int DeletedTextLogsByCount,
        string LogRoot,
        string StoragePolicy);
}

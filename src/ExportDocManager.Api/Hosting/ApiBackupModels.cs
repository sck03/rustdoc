namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiBackupListResponse(
        IReadOnlyList<ApiBackupItemDto> Backups,
        string BackupRoot,
        string StoragePolicy);

    public sealed record ApiBackupItemDto(
        string FileName,
        string FullPath,
        long SizeBytes,
        DateTime CreatedAt,
        DateTime LastWriteTime);

    public sealed record ApiBackupCreateResponse(
        bool Success,
        string Message,
        IReadOnlyList<ApiBackupItemDto> Backups,
        string BackupRoot,
        string StoragePolicy);

    public sealed record ApiBackupCleanupRequest(int DaysToKeep);

    public sealed record ApiBackupRestoreRequest(
        string BackupFileName,
        string ConfirmationText);

    public sealed record ApiCloudBackupListResponse(
        IReadOnlyList<ApiCloudBackupItemDto> Backups,
        string BackupRoot,
        string StoragePolicy);

    public sealed record ApiCloudBackupItemDto(
        string FileName,
        long SizeBytes,
        DateTime LastModified);

    public sealed record ApiCloudBackupDownloadRequest(string RemoteFileName);

    public sealed record ApiCloudBackupStatusResponse(
        bool Enabled,
        bool IsConfigured,
        string Url,
        string UserName,
        string LatestBackupFileName,
        long LatestBackupSizeBytes,
        string BackupRoot,
        string StoragePolicy);

    public sealed record ApiCloudBackupCommandResponse(
        bool Success,
        string Message,
        string RemoteFileName,
        string LocalBackupPath,
        long SizeBytes,
        string BackupRoot,
        string StoragePolicy);
}

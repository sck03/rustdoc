namespace ExportDocManager.Services.Infrastructure
{
    public sealed record ServerMigrationRequestContext(
        string RequestedBy,
        string RemoteAddress);

    public sealed record ServerMigrationStatus(
        bool Supported,
        bool PostgreSqlConfigured,
        bool ToolsReady,
        bool PendingRestore,
        string PackageRoot,
        string Message,
        string StoragePolicy,
        string RestorePhase,
        string RestoreDetail,
        DateTimeOffset? RestoreUpdatedAtUtc);

    public sealed record ServerMigrationPackageResult(
        bool Success,
        string Message,
        string FileName,
        string FullPath,
        long SizeBytes,
        string PackageRoot,
        string StoragePolicy);

    public sealed record ServerMigrationRestoreResult(
        bool Success,
        bool RestartRequired,
        string Message,
        string PackageFileName,
        string SafetyBackupRoot,
        string StoragePolicy);

    public interface IServerMigrationService
    {
        ServerMigrationStatus GetStatus();

        Task<ServerMigrationPackageResult> CreatePackageAsync(
            string password,
            ServerMigrationRequestContext requestContext,
            CancellationToken cancellationToken = default);

        Task<ServerMigrationRestoreResult> StageRestoreAsync(
            Stream package,
            string packageFileName,
            string password,
            ServerMigrationRequestContext requestContext,
            CancellationToken cancellationToken = default,
            long? expectedPackageBytes = null);

        Task<ServerMigrationRestoreResult> StageDatabaseRestoreAsync(
            Stream databaseBackup,
            string backupFileName,
            ServerMigrationRequestContext requestContext,
            CancellationToken cancellationToken = default,
            long? expectedBackupBytes = null);
    }
}

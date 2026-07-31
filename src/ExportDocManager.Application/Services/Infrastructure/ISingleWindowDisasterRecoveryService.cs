namespace ExportDocManager.Services.Infrastructure
{
    public interface ISingleWindowDisasterRecoveryService
    {
        SingleWindowDisasterRecoveryStatus GetStatus();

        Task<SingleWindowDisasterRecoveryPackageResult> CreatePackageAsync(
            string password,
            CancellationToken cancellationToken = default);

        Task<SingleWindowDisasterRecoveryRestoreResult> ScheduleRestoreAsync(
            string packagePath,
            string password,
            CancellationToken cancellationToken = default);
    }

    public sealed record SingleWindowDisasterRecoveryStatus(
        bool Supported,
        bool UsesSqlite,
        bool PendingRestore,
        string RecoveryRoot,
        string Message,
        string StoragePolicy);

    public sealed record SingleWindowDisasterRecoveryPackageResult(
        bool Success,
        string Message,
        string FileName,
        string FilePath,
        long SizeBytes,
        string StoragePolicy);

    public sealed record SingleWindowDisasterRecoveryRestoreResult(
        bool Success,
        bool RestartRequired,
        string Message,
        string PackageFileName,
        string SafetyBackupRoot,
        string StoragePolicy);
}

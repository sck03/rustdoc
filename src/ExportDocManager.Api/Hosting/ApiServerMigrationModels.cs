namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiServerMigrationStatusResponse(
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

    public sealed record ApiServerMigrationCreateRequest(
        string Password,
        string AdminPassword,
        string ConfirmationText);

    public sealed record ApiSensitiveOperationAuthorizationRequest(
        string Action,
        string AdminPassword);

    public sealed record ApiSensitiveOperationAuthorizationResponse(
        string Action,
        string Ticket,
        DateTimeOffset ExpiresAtUtc);

    public sealed record ApiPostgreSqlDatabaseRestoreRequest(
        string BackupFileName,
        string AdminPassword,
        string ConfirmationText);

    public sealed record ApiServerMigrationRestoreResponse(
        bool Success,
        bool RestartRequired,
        bool AutomaticRestartScheduled,
        string Message,
        string PackageFileName,
        string SafetyBackupRoot,
        string StoragePolicy);
}

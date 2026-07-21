namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSharedDatabaseBackupItemDto(
        string FileName,
        string FullPath,
        long SizeBytes,
        DateTime CreatedAt,
        DateTime LastWriteTime);

    public sealed record ApiSharedDatabaseOwnershipSummaryResponse(
        int TotalInvoices,
        int UnassignedInvoices,
        int TotalPayments,
        int UnassignedPayments,
        int TotalOtherBusinessData,
        int UnassignedOtherBusinessData,
        IReadOnlyList<ApiSharedDatabaseOwnerSummaryItemDto> Owners,
        string StoragePolicy);

    public sealed record ApiSharedDatabaseOwnerSummaryItemDto(
        int UserId,
        string Username,
        string FullName,
        string Role,
        string DepartmentId,
        string CompanyScope,
        bool IsActive,
        int InvoiceCount,
        int PaymentCount,
        int OtherBusinessDataCount);

    public sealed record ApiSharedDatabaseOwnershipTransferRequest(
        int? FromUserId,
        int ToUserId,
        bool IncludeInvoices,
        bool IncludePayments,
        bool IncludeOtherBusinessData,
        bool OnlyUnassigned,
        string DepartmentId,
        string CompanyScope,
        string ConfirmationText);

    public sealed record ApiSharedDatabaseOwnershipTransferResponse(
        bool Success,
        string Message,
        int UpdatedInvoices,
        int UpdatedPayments,
        int UpdatedOtherBusinessData,
        string StoragePolicy);

    public sealed record ApiSupportPackageResponse(
        bool Success,
        string Message,
        string FileName,
        string FullPath,
        long SizeBytes,
        string SupportPackageRoot,
        string StoragePolicy);

    public sealed record ApiSupportPackageRequest(
        bool IncludeLatestDatabaseBackup,
        bool IncludeSampleFiles,
        string ConfirmationText);

    public sealed record ApiPostgreSqlMaintenanceStatusResponse(
        bool PostgreSqlSelected,
        bool PostgreSqlConfigured,
        string Host,
        int Port,
        string Database,
        string Username,
        string BackupRoot,
        string ToolBinRoot,
        string PgDumpPath,
        string PgRestorePath,
        string PsqlPath,
        bool ToolsReady,
        string StoragePolicy);

    public sealed record ApiPostgreSqlPhysicalBackupListResponse(
        ApiPostgreSqlMaintenanceStatusResponse Status,
        IReadOnlyList<ApiSharedDatabaseBackupItemDto> Backups);

    public sealed record ApiPostgreSqlPhysicalBackupResponse(
        bool Success,
        string Message,
        string FileName,
        string FullPath,
        long SizeBytes,
        string BackupRoot,
        string StoragePolicy);

    public sealed record ApiPostgreSqlRestorePlanRequest(
        string BackupFileName,
        string TargetDatabase,
        string ApplicationRole,
        IReadOnlyList<string> OldOwnerRoles);

    public sealed record ApiPostgreSqlRestorePlanResponse(
        bool Success,
        string Message,
        string PlanRoot,
        string RestoreScriptPath,
        string OwnershipSqlPath,
        string BackupFilePath,
        string StoragePolicy);
}

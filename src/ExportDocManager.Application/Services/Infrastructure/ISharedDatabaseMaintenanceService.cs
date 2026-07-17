using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed record SharedDatabaseBackupItem(
        string FileName,
        string FullPath,
        long SizeBytes,
        DateTime CreatedAt,
        DateTime LastWriteTime);

    public sealed record SharedDatabaseOwnershipSummary(
        int TotalInvoices,
        int UnassignedInvoices,
        int TotalPayments,
        int UnassignedPayments,
        IReadOnlyList<SharedDatabaseOwnerSummaryItem> Owners,
        string StoragePolicy);

    public sealed record SharedDatabaseOwnerSummaryItem(
        int UserId,
        string Username,
        string FullName,
        string Role,
        string DepartmentId,
        string CompanyScope,
        int InvoiceCount,
        int PaymentCount);

    public sealed record SharedDatabaseOwnershipTransferResult(
        bool Success,
        string Message,
        int UpdatedInvoices,
        int UpdatedPayments,
        string StoragePolicy);

    public sealed record SupportPackageResult(
        bool Success,
        string Message,
        string FileName,
        string FullPath,
        long SizeBytes,
        string SupportPackageRoot,
        string StoragePolicy);

    public sealed record PostgreSqlMaintenanceStatus(
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

    public sealed record PostgreSqlPhysicalBackupResult(
        bool Success,
        string Message,
        string FileName,
        string FullPath,
        long SizeBytes,
        string BackupRoot,
        string StoragePolicy);

    public sealed record PostgreSqlRestorePlanResult(
        bool Success,
        string Message,
        string PlanRoot,
        string RestoreScriptPath,
        string OwnershipSqlPath,
        string BackupFilePath,
        string StoragePolicy);

    public sealed class PostgreSqlRestorePlanRequest
    {
        public string BackupFileName { get; set; } = string.Empty;
        public string TargetDatabase { get; set; } = string.Empty;
        public string ApplicationRole { get; set; } = string.Empty;
        public IReadOnlyList<string> OldOwnerRoles { get; set; } = Array.Empty<string>();
    }

    public sealed class SupportPackageOptions
    {
        public bool IncludeLatestDatabaseBackup { get; set; }
        public bool IncludeSampleFiles { get; set; }
    }

    public sealed class SharedDatabaseOwnershipTransferRequest
    {
        public int? FromUserId { get; set; }
        public int ToUserId { get; set; }
        public bool IncludeInvoices { get; set; } = true;
        public bool IncludePayments { get; set; } = true;
        public bool OnlyUnassigned { get; set; }
        public string DepartmentId { get; set; } = string.Empty;
        public string CompanyScope { get; set; } = string.Empty;
    }

    public interface ISharedDatabaseMaintenanceService
    {
        bool IsSharedDatabaseEnabled { get; }

        string SupportPackageRoot { get; }

        Task<SharedDatabaseOwnershipSummary> GetOwnershipSummaryAsync(CancellationToken cancellationToken = default);

        Task<SharedDatabaseOwnershipTransferResult> TransferOwnershipAsync(
            SharedDatabaseOwnershipTransferRequest request,
            CancellationToken cancellationToken = default);

        Task<SupportPackageResult> CreateSupportPackageAsync(CancellationToken cancellationToken = default);

        PostgreSqlMaintenanceStatus GetPostgreSqlMaintenanceStatus();

        IReadOnlyList<SharedDatabaseBackupItem> ListPostgreSqlPhysicalBackups();

        Task<PostgreSqlPhysicalBackupResult> CreatePostgreSqlPhysicalBackupAsync(CancellationToken cancellationToken = default);

        Task<PostgreSqlRestorePlanResult> CreatePostgreSqlRestorePlanAsync(
            PostgreSqlRestorePlanRequest request,
            CancellationToken cancellationToken = default);

        Task<SupportPackageResult> CreateSupportPackageAsync(
            SupportPackageOptions options,
            CancellationToken cancellationToken = default);
    }
}

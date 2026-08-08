namespace ExportDocManager.Services.Infrastructure
{
    internal static class ServerMigrationLayout
    {
        public const int SchemaVersion = 2;
        public const string PackageExtension = ".edmmigration";
        public const string ManifestEntry = "manifest.json";
        public const string DatabaseEntry = "Database/postgresql.dump";
        public const string PendingMarkerFileName = "pending-server-migration.json";
        public const string LockFileName = "server-migration.lock";
        public const string StatusFileName = "server-migration-status.json";
        public const string ControlDirectoryName = "ServerMigration";
        public const string PackageDirectoryName = "ServerMigration";
        public const string StoragePolicy =
            "服务器迁移包使用 PBKDF2-SHA256 与 AES-256-GCM 分块加密，包含 PostgreSQL 业务库、运行配置、业务文件、用户模板、印章、唛头图片、本地主密钥和单一窗口运行数据；不包含日志、缓存、导出临时文件、备份历史、许可证、机器绑定试用文件或 TLS/Certbot 证书。恢复前会验证产品数据库版本并创建数据库与文件安全备份，失败时自动回滚。";

        public static string DataEntry(string category, string relativePath) =>
            $"Data/{category}/{relativePath.Replace('\\', '/')}";

        public static string ConfigEntry(string relativePath) =>
            $"Config/{relativePath.Replace('\\', '/')}";

        public static string SecurityEntry(string relativePath) =>
            $"Security/{relativePath.Replace('\\', '/')}";
    }

    internal static class ServerMigrationRestorePhase
    {
        public const string Pending = "pending";
        public const string Validating = "validating";
        public const string SafetyBackup = "safety-backup";
        public const string ApplyingDatabase = "applying-database";
        public const string ApplyingFiles = "applying-files";
        public const string RollingBack = "rolling-back";
        public const string Completed = "completed";
        public const string Failed = "failed";

        public static bool IsKnown(string phase) => phase is
            Pending or Validating or SafetyBackup or ApplyingDatabase or ApplyingFiles or
            RollingBack or Completed or Failed;

        public static bool IsActive(string phase) => phase is
            Pending or Validating or SafetyBackup or ApplyingDatabase or ApplyingFiles or RollingBack;

        public static bool IsTerminal(string phase) => phase is Completed or Failed;
    }

    internal sealed class ServerMigrationManifest
    {
        public int SchemaVersion { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string SourceDataRoot { get; set; } = string.Empty;
        public string SourcePlatform { get; set; } = string.Empty;
        public bool? SourcePathCaseSensitive { get; set; }
        public List<ServerMigrationFileManifest> Files { get; set; } = [];
    }

    internal sealed class ServerMigrationFileManifest
    {
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    internal sealed class PendingServerMigrationRestore
    {
        public int SchemaVersion { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public string PackageFileName { get; set; } = string.Empty;
        public DateTimeOffset ScheduledAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string StagingDirectoryName { get; set; } = string.Empty;
        public string Phase { get; set; } = ServerMigrationRestorePhase.Pending;
        public int Attempt { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
        public string RemoteAddress { get; set; } = string.Empty;
        public string ValidationDatabaseName { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
        public bool ManualRecoveryRequired { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public string SafetyBackupRoot { get; set; } = string.Empty;
        public ServerMigrationManifest Manifest { get; set; } = new();
    }

    internal sealed class ServerMigrationRestoreStatusSnapshot
    {
        public int SchemaVersion { get; set; } = ServerMigrationLayout.SchemaVersion;
        public string PackageId { get; set; } = string.Empty;
        public string PackageFileName { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string Message { get; set; } = string.Empty;
        public string SafetyBackupRoot { get; set; } = string.Empty;
    }
}

namespace ExportDocManager.Services.SingleWindow
{
    internal static class SingleWindowDisasterRecoveryLayout
    {
        public const int SchemaVersion = 1;
        public const string PackageExtension = ".edmrecovery";
        public const string ManifestEntry = "manifest.json";
        public const string AppSettingsEntry = "Config/appsettings.json";
        public const string MasterKeyEntry = "Security/local-master-key.bin";
        public const string StationIdentityEntry = "Security/SingleWindow/station.id";
        public const string RecoveryDirectoryName = "DisasterRecovery";
        public const string ControlDirectoryName = "Recovery";
        public const string PendingMarkerFileName = "pending-disaster-recovery.json";
        public const string SafetyCompleteFileName = ".safety-copy-complete";
        public const string StoragePolicy =
            "持卡机灾难恢复包与普通数据库 ZIP 备份严格分离，使用 PBKDF2-SHA256 派生密钥并以 AES-256-GCM 分块认证加密；只包含 SQLite 一致性快照、station.id、本地主密钥和 Config/appsettings.json，不包含注册码、试用锚点、机器种子、机器绑定文件或任何私钥。";

        public static string DatabaseEntry(string databaseFileName) =>
            $"Database/{databaseFileName}";
    }

    internal sealed class DisasterRecoveryPackageManifest
    {
        public int SchemaVersion { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string DatabaseFileName { get; set; } = string.Empty;
        public string StationKey { get; set; } = string.Empty;
        public List<DisasterRecoveryFileManifest> Files { get; set; } = [];
        public string LicensePolicy { get; set; } = string.Empty;
    }

    internal sealed class DisasterRecoveryFileManifest
    {
        public string RelativePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    internal sealed class PendingDisasterRecoveryRestore
    {
        public int SchemaVersion { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public string PackageFileName { get; set; } = string.Empty;
        public DateTimeOffset ScheduledAtUtc { get; set; }
        public string StagingDirectoryName { get; set; } = string.Empty;
        public string DatabaseFileName { get; set; } = string.Empty;
        public List<DisasterRecoveryFileManifest> Files { get; set; } = [];
    }
}

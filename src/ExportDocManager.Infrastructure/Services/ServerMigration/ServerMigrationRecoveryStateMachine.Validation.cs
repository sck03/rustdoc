using System.Security.Cryptography;
using ExportDocManager.Services;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Services.Infrastructure;

public static partial class ServerMigrationRecoveryStateMachine
{
    private static void ValidateStagedManifest(
        string stagingRoot,
        ServerMigrationManifest manifest)
    {
        if (manifest is null ||
            manifest.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
            manifest.Files is null ||
            manifest.Files.Count == 0 ||
            manifest.Files.Any(file => file is null) ||
            !manifest.Files.Any(file => file.RelativePath.Equals(
                ServerMigrationLayout.DatabaseEntry,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("服务器迁移清单缺少数据库备份或版本无效。");
        }

        bool fullMigration = manifest.Files.Any(file =>
            !file.RelativePath.Equals(
                ServerMigrationLayout.DatabaseEntry,
                StringComparison.OrdinalIgnoreCase));
        if (fullMigration &&
            (!manifest.Files.Any(file => file.RelativePath.Equals(
                ServerMigrationLayout.ConfigEntry("appsettings.json"),
                StringComparison.OrdinalIgnoreCase)) ||
             !manifest.Files.Any(file => file.RelativePath.Equals(
                ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName),
                StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("服务器完整迁移清单缺少运行配置或本地主密钥。");
        }

        foreach (ServerMigrationFileManifest file in manifest.Files)
        {
            _ = ServerMigrationPackageValidator.NormalizeRelativePath(file.RelativePath);
            string path = ServerMigrationPackageValidator.ResolvePath(stagingRoot, file.RelativePath);
            if (!File.Exists(path) ||
                new FileInfo(path).Length != file.SizeBytes ||
                !string.Equals(
                    ServerMigrationPackageValidator.ComputeSha256(path),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"服务器迁移暂存文件校验失败：{file.RelativePath}");
            }
        }
    }

    private static void ValidateMasterKeyCompatibility(
        string stagingRoot,
        ServerMigrationManifest manifest)
    {
        if (manifest?.Files is null || manifest.Files.Any(file => file is null))
        {
            throw new InvalidDataException("服务器迁移清单文件列表无效。");
        }

        string masterKeyEntry = ServerMigrationLayout.SecurityEntry(
            LocalSecretProtector.MasterKeyFileName);
        bool fullMigration = manifest.Files.Any(file =>
            !file.RelativePath.Equals(
                ServerMigrationLayout.DatabaseEntry,
                StringComparison.OrdinalIgnoreCase));
        ServerMigrationFileManifest? masterKey = manifest.Files.FirstOrDefault(file =>
            file.RelativePath.Equals(masterKeyEntry, StringComparison.OrdinalIgnoreCase));
        if (!fullMigration)
        {
            return;
        }
        if (masterKey == null)
        {
            throw new InvalidDataException("服务器迁移包缺少本地主密钥。");
        }

        string configured = Environment.GetEnvironmentVariable(
            LocalSecretProtector.MasterKeyEnvironmentVariable)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        byte[] configuredKey = ServerMigrationService.ParseConfiguredMasterKey(configured);
        byte[] packageKey = File.ReadAllBytes(ServerMigrationPackageValidator.ResolvePath(
            stagingRoot,
            masterKeyEntry));
        try
        {
            if (packageKey.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(configuredKey, packageKey))
            {
                throw new ServiceValidationException(
                    "目标服务器的 EXPORTDOCMANAGER_MASTER_KEY 与迁移包不一致；数据库尚未恢复，请使用源服务器主密钥重新部署目标服务。");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(configuredKey);
            CryptographicOperations.ZeroMemory(packageKey);
        }
    }
}

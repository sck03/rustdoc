using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 迁移包的边界校验器：大小、路径、清单、哈希和部署证书排除全部集中在此处。
/// </summary>
internal static class ServerMigrationPackageValidator
{
    public const long MaximumPackageBytes =
        DisasterRecoveryPackageCrypto.MaximumPlaintextBytes + 32L * 1024L * 1024L;
    public const long MaximumManifestBytes = 32L * 1024L * 1024L;

    public static readonly ZipExtractionLimits ExtractionLimits = new(
        MaximumEntries: 100_000,
        MaximumEntryBytes: DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
        MaximumTotalBytes: DisasterRecoveryPackageCrypto.MaximumPlaintextBytes,
        MaximumCompressionRatio: 2_000,
        MaximumPathDepth: 12);

    public static async Task CopyBoundedAsync(
        Stream source,
        string destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new PayloadLimitExceededException(maximumBytes);
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ServerMigrationManifest> ReadAndValidateManifestAsync(
        string extractedRoot,
        CancellationToken cancellationToken)
    {
        string path = ResolvePath(extractedRoot, ServerMigrationLayout.ManifestEntry);
        var manifest = JsonSerializer.Deserialize<ServerMigrationManifest>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
            ServerMigrationService.JsonOptions) ?? throw new InvalidDataException("服务器迁移包清单为空。");
        if (manifest.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
            !Guid.TryParseExact(manifest.PackageId, "N", out _))
        {
            throw new InvalidDataException("服务器迁移包清单版本或包 ID 无效。");
        }
        if (manifest.Files is null ||
            manifest.Files.Count == 0 ||
            manifest.Files.Count > ExtractionLimits.MaximumEntries ||
            manifest.Files.Any(file => file is null))
        {
            throw new InvalidDataException("服务器迁移包文件清单为空或过大。");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ServerMigrationFileManifest file in manifest.Files)
        {
            string normalized = NormalizeRelativePath(file.RelativePath);
            ServerMigrationDeploymentCertificateAdapter.EnsureNotIncluded(normalized);
            bool allowed = normalized.Equals(ServerMigrationLayout.DatabaseEntry, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Config/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName), StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(ServerMigrationLayout.SecurityEntry("SingleWindow/station.id"), StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Data/Files/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Data/Templates/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Data/SingleWindow/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Data/Marks/", StringComparison.OrdinalIgnoreCase);
            bool allowsEmptyContent = normalized.StartsWith("Data/", StringComparison.OrdinalIgnoreCase);
            if (!names.Add(normalized) ||
                (allowsEmptyContent ? file.SizeBytes < 0 : file.SizeBytes <= 0) ||
                string.IsNullOrWhiteSpace(file.Sha256) ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit) ||
                !allowed)
            {
                throw new InvalidDataException($"服务器迁移包文件清单无效：{file.RelativePath}");
            }
            string source = ResolvePath(extractedRoot, normalized);
            if (!File.Exists(source) ||
                new FileInfo(source).Length != file.SizeBytes ||
                !string.Equals(
                    await ComputeSha256Async(source, cancellationToken).ConfigureAwait(false),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"服务器迁移包文件校验失败：{file.RelativePath}");
            }
        }
        if (!names.Contains(ServerMigrationLayout.DatabaseEntry))
        {
            throw new InvalidDataException("服务器迁移包缺少 PostgreSQL 数据库备份。");
        }
        if (!names.Contains(ServerMigrationLayout.ConfigEntry("appsettings.json")))
        {
            throw new InvalidDataException("服务器迁移包缺少运行配置 appsettings.json。");
        }
        if (!names.Contains(ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName)))
        {
            throw new InvalidDataException("服务器迁移包缺少本地主密钥。");
        }
        return manifest;
    }

    public static void ValidateArchiveEntries(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count < 2 || archive.Entries.Count > ExtractionLimits.MaximumEntries)
        {
            throw new InvalidDataException("服务器迁移包内部条目数量无效。");
        }
        if (archive.Entries.Any(item => string.IsNullOrEmpty(item.Name)))
        {
            throw new InvalidDataException("服务器迁移包不能包含目录条目。");
        }
        var fileEntries = archive.Entries
            .Select(item => new
            {
                Entry = item,
                Name = item.FullName.Replace('\\', '/').Trim('/')
            })
            .ToList();
        var names = fileEntries.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestEntries = fileEntries.Where(item => item.Name.Equals(ServerMigrationLayout.ManifestEntry, StringComparison.OrdinalIgnoreCase)).ToList();
        if (manifestEntries.Count != 1 ||
            manifestEntries[0].Entry.Length <= 0 ||
            manifestEntries[0].Entry.Length > MaximumManifestBytes ||
            !names.Contains(ServerMigrationLayout.DatabaseEntry))
        {
            throw new InvalidDataException("服务器迁移包缺少唯一清单或 PostgreSQL 数据库备份。");
        }
        using Stream manifestStream = manifestEntries[0].Entry.Open();
        ServerMigrationManifest manifest = JsonSerializer.Deserialize<ServerMigrationManifest>(manifestStream, ServerMigrationService.JsonOptions)
            ?? throw new InvalidDataException("服务器迁移包清单为空。");
        if (manifest.Files is null || manifest.Files.Any(file => file is null))
        {
            throw new InvalidDataException("服务器迁移包清单文件列表无效。");
        }
        foreach (var item in fileEntries)
        {
            ServerMigrationDeploymentCertificateAdapter.EnsureNotIncluded(NormalizeRelativePath(item.Name));
        }
        var expectedNames = manifest.Files
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .Append(ServerMigrationLayout.ManifestEntry)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expectedNames.Count != fileEntries.Count || fileEntries.Any(item => !expectedNames.Contains(item.Name)))
        {
            throw new InvalidDataException("服务器迁移包包含清单之外的文件。");
        }
    }

    public static long GetDeclaredUncompressedBytes(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (entry.Length < 0 || entry.Length > ExtractionLimits.MaximumEntryBytes)
            {
                throw new PayloadLimitExceededException(ExtractionLimits.MaximumEntryBytes);
            }

            try
            {
                total = checked(total + entry.Length);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("迁移包展开总大小超出安全上限。", ex);
            }
            if (total > ExtractionLimits.MaximumTotalBytes)
            {
                throw new PayloadLimitExceededException(ExtractionLimits.MaximumTotalBytes);
            }
        }
        return total;
    }

    public static string ResolvePath(string root, string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string fullRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!PathBoundaryHelper.IsWithinRoot(path, fullRoot))
        {
            throw new InvalidDataException("服务器迁移包路径越界。");
        }
        return path;
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        string normalized = (relativePath ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized) || normalized.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("服务器迁移包相对路径无效。");
        }
        string[] segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Length > ExtractionLimits.MaximumPathDepth || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOf(':') >= 0 || segment.Any(char.IsControl)))
        {
            throw new InvalidDataException("服务器迁移包相对路径无效。");
        }
        return string.Join('/', segments);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

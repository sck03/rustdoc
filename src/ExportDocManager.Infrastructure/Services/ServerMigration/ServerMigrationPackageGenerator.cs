using System.Security.Cryptography;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 完整迁移包生成器。只负责收集受控运行目录、生成清单、压缩并加密，不参与恢复。
/// </summary>
internal sealed class ServerMigrationPackageGenerator
{
    internal static readonly long MaximumSourceBytes =
        ServerMigrationPackageValidator.ExtractionLimits.MaximumTotalBytes -
        ServerMigrationPackageValidator.MaximumManifestBytes;

    private readonly IAppPathProvider _paths;
    private readonly ISharedDatabaseMaintenanceService _databaseMaintenance;
    private readonly string _packageRoot;

    public ServerMigrationPackageGenerator(
        IAppPathProvider paths,
        ISharedDatabaseMaintenanceService databaseMaintenance,
        string packageRoot)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _databaseMaintenance = databaseMaintenance ?? throw new ArgumentNullException(nameof(databaseMaintenance));
        _packageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
    }

    public async Task<ServerMigrationPackageResult> CreateAsync(
        string password,
        string packageId,
        string packagePath,
        string workingRoot,
        string payloadPath,
        CancellationToken cancellationToken)
    {
        var databaseBackup = await _databaseMaintenance
            .CreatePostgreSqlPhysicalBackupAsync(cancellationToken)
            .ConfigureAwait(false);
        var sources = new List<(string SourcePath, string EntryName)>();
        long collectedBytes = 0;
        AddSource(sources, databaseBackup.FullPath, ServerMigrationLayout.DatabaseEntry, ref collectedBytes);
        AddDirectoryFiles(sources, _paths.ConfigRoot, ServerMigrationLayout.ConfigEntry, ref collectedBytes);
        AddDirectoryFiles(sources, _paths.FileRoot, relative => ServerMigrationLayout.DataEntry("Files", relative), ref collectedBytes);
        AddDirectoryFiles(sources, _paths.UserTemplateRoot, relative => ServerMigrationLayout.DataEntry("Templates", relative), ref collectedBytes);
        AddDirectoryFiles(sources, _paths.SingleWindowRoot, relative => ServerMigrationLayout.DataEntry("SingleWindow", relative), ref collectedBytes);
        AddDirectoryFiles(sources, Path.Combine(_paths.DataRoot, "Marks"), relative => ServerMigrationLayout.DataEntry("Marks", relative), ref collectedBytes);

        string masterKeyPath = Path.Combine(_paths.SecurityRoot, LocalSecretProtector.MasterKeyFileName);
        EnsureDirectoryRootIsNotLink(_paths.SecurityRoot);
        EnsureMasterKeyFile(masterKeyPath);
        AddSource(
            sources,
            masterKeyPath,
            ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName),
            ref collectedBytes);
        string stationPath = Path.Combine(_paths.SecurityRoot, "SingleWindow", "station.id");
        if (File.Exists(stationPath))
        {
            EnsureFileIsNotLink(stationPath);
            AddSource(
                sources,
                stationPath,
                ServerMigrationLayout.SecurityEntry("SingleWindow/station.id"),
                ref collectedBytes);
        }

        var manifest = new ServerMigrationManifest
        {
            SchemaVersion = ServerMigrationLayout.SchemaVersion,
            PackageId = packageId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceDataRoot = _paths.DataRoot,
            SourcePlatform = GetCurrentPlatformName(),
            SourcePathCaseSensitive = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()
        };
        long hashedBytes = 0;
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            (string sourcePath, string entryName) = sources[sourceIndex];
            var info = new FileInfo(sourcePath);
            bool allowsEmptyContent = entryName.StartsWith("Data/", StringComparison.OrdinalIgnoreCase);
            if (!info.Exists || !allowsEmptyContent && info.Length <= 0)
            {
                throw new InvalidDataException($"迁移源文件不存在或为空：{sourcePath}");
            }
            hashedBytes = ValidateNextSourceBudget(sourceIndex, hashedBytes, info.Length);
            manifest.Files.Add(new ServerMigrationFileManifest
            {
                RelativePath = entryName,
                SizeBytes = info.Length,
                Sha256 = await ServerMigrationPackageValidator.ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false)
            });
        }

        string manifestPath = Path.Combine(workingRoot, ServerMigrationLayout.ManifestEntry);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ServerMigrationService.JsonOptions),
            cancellationToken).ConfigureAwait(false);
        sources.Add((manifestPath, ServerMigrationLayout.ManifestEntry));
        long manifestBytes = new FileInfo(manifestPath).Length;
        RuntimeStorageBudget.EnsureAvailable(
            _packageRoot,
            RuntimeStorageBudget.WithSafetyMargin(collectedBytes, manifestBytes, collectedBytes),
            "生成服务器迁移包");
        await ZipArchiveHelper.CreateFromFilesAsync(sources, payloadPath, cancellationToken).ConfigureAwait(false);
        await AtomicFileHelper.WriteFileAtomicAsync(
            packagePath,
            (tempPath, ct) => DisasterRecoveryPackageCrypto.EncryptAsync(payloadPath, tempPath, password, ct),
            cancellationToken).ConfigureAwait(false);
        RuntimeFilePermissionHelper.RestrictFile(packagePath);
        var package = new FileInfo(packagePath);
        return new ServerMigrationPackageResult(
            true,
            "服务器加密迁移包已创建。请将迁移包和加密密码分开保管。",
            package.Name,
            package.FullName,
            package.Length,
            _packageRoot,
            ServerMigrationDeploymentCertificateAdapter.StoragePolicy);
    }

    private static void AddDirectoryFiles(
        ICollection<(string SourcePath, string EntryName)> sources,
        string root,
        Func<string, string> entryFactory,
        ref long collectedBytes)
    {
        if (!Directory.Exists(root)) return;
        string fullRoot = Path.GetFullPath(root);
        EnsureDirectoryRootIsNotLink(fullRoot);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(fullRoot);
        while (pendingDirectories.Count > 0)
        {
            string directory = pendingDirectories.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ServiceValidationException($"服务器迁移源目录不能包含符号链接或重解析点：{entry}");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push(entry);
                    continue;
                }
                string relative = Path.GetRelativePath(fullRoot, entry);
                AddSource(sources, entry, entryFactory(relative), ref collectedBytes);
            }
        }
    }

    private static void AddSource(
        ICollection<(string SourcePath, string EntryName)> sources,
        string sourcePath,
        string entryName,
        ref long collectedBytes)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("服务器迁移源文件不存在。", sourcePath);
        }

        collectedBytes = ValidateNextSourceBudget(sources.Count, collectedBytes, info.Length);
        sources.Add((sourcePath, entryName));
    }

    internal static long ValidateNextSourceBudget(
        int currentSourceCount,
        long currentBytes,
        long nextFileBytes)
    {
        if (currentSourceCount < 0 || currentBytes < 0 || nextFileBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentSourceCount));
        }

        // Reserve one ZIP entry for manifest.json and reserve its maximum
        // uncompressed size before performing any potentially expensive hash.
        if (currentSourceCount >= ServerMigrationPackageValidator.ExtractionLimits.MaximumEntries - 1)
        {
            throw new InvalidDataException("服务器迁移源文件数量超过安全上限。");
        }

        long nextTotal;
        try
        {
            nextTotal = checked(currentBytes + nextFileBytes);
        }
        catch (OverflowException)
        {
            throw new PayloadLimitExceededException(MaximumSourceBytes);
        }
        if (nextFileBytes > ServerMigrationPackageValidator.ExtractionLimits.MaximumEntryBytes ||
            nextTotal > MaximumSourceBytes)
        {
            throw new PayloadLimitExceededException(MaximumSourceBytes);
        }

        return nextTotal;
    }

    private static void EnsureMasterKeyFile(string path)
    {
        string configured = Environment.GetEnvironmentVariable(LocalSecretProtector.MasterKeyEnvironmentVariable)?.Trim() ?? string.Empty;
        byte[]? configuredKey = string.IsNullOrWhiteSpace(configured)
            ? null
            : ServerMigrationService.ParseConfiguredMasterKey(configured);
        try
        {
            if (File.Exists(path))
            {
                EnsureFileIsNotLink(path);
                byte[] fileKey = File.ReadAllBytes(path);
                try
                {
                    if (fileKey.Length != 32) throw new InvalidDataException("本地主密钥文件长度无效。");
                    if (configuredKey != null && !CryptographicOperations.FixedTimeEquals(configuredKey, fileKey))
                    {
                        throw new ServiceValidationException("EXPORTDOCMANAGER_MASTER_KEY 与本地主密钥文件不一致，不能创建可恢复的服务器迁移包。");
                    }
                    return;
                }
                finally { CryptographicOperations.ZeroMemory(fileKey); }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] key = configuredKey ?? RandomNumberGenerator.GetBytes(32);
            try
            {
                AtomicFileHelper.WriteFileAtomic(path, temp => File.WriteAllBytes(temp, key));
                RuntimeFilePermissionHelper.RestrictFile(path);
            }
            finally
            {
                if (!ReferenceEquals(key, configuredKey)) CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            if (configuredKey != null) CryptographicOperations.ZeroMemory(configuredKey);
        }
    }

    private static void EnsureDirectoryRootIsNotLink(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ServiceValidationException($"服务器迁移源目录不能是符号链接或重解析点：{path}");
        }
    }

    private static string GetCurrentPlatformName() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "macos"
            : "linux";

    private static void EnsureFileIsNotLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ServiceValidationException($"服务器迁移源文件不能是符号链接或重解析点：{path}");
        }
    }
}

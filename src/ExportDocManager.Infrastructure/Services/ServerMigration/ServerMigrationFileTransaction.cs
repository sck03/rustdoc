using System.Text.Json;
using System.Text.Json.Nodes;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    internal static class ServerMigrationFileTransaction
    {
        private const string StateFileName = "file-transaction.json";

        public static ServerMigrationFileTransactionState Prepare(
            IAppPathProvider paths,
            string stagingRoot,
            string safetyRoot,
            PendingServerMigrationRestore marker,
            DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(marker);
            bool fullMigration = marker.Manifest.Files.Any(file =>
                !file.RelativePath.Equals(
                    ServerMigrationLayout.DatabaseEntry,
                    StringComparison.OrdinalIgnoreCase));
            var state = new ServerMigrationFileTransactionState
            {
                PackageId = marker.PackageId,
                FullMigration = fullMigration
            };
            if (!fullMigration)
            {
                WriteState(safetyRoot, state);
                return state;
            }

            Directory.CreateDirectory(safetyRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(safetyRoot);
            WriteState(safetyRoot, state);
            try
            {
                foreach ((string prefix, string targetRoot, string safetyName) in new[]
                {
                    ("Data/Files/", paths.FileRoot, "Files"),
                    ("Data/Templates/", paths.UserTemplateRoot, "Templates"),
                    ("Data/SingleWindow/", paths.SingleWindowRoot, "SingleWindow"),
                    ("Data/Marks/", Path.Combine(paths.DataRoot, "Marks"), "Marks")
                })
                {
                    ServerMigrationDirectoryTransaction directory = CreateDirectoryTransaction(
                        safetyRoot,
                        marker,
                        targetRoot,
                        safetyName);
                    state.Directories.Add(directory);
                    WriteState(safetyRoot, state);
                    PrepareDirectory(stagingRoot, marker, prefix, directory);
                }

                foreach (ServerMigrationFileManifest file in marker.Manifest.Files)
                {
                    if (!file.RelativePath.StartsWith("Config/", StringComparison.OrdinalIgnoreCase) &&
                        !file.RelativePath.StartsWith("Security/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string targetRoot;
                    string relative;
                    if (file.RelativePath.StartsWith("Config/", StringComparison.OrdinalIgnoreCase))
                    {
                        targetRoot = paths.ConfigRoot;
                        relative = file.RelativePath["Config/".Length..];
                    }
                    else
                    {
                        targetRoot = paths.SecurityRoot;
                        relative = file.RelativePath["Security/".Length..];
                    }
                    ServerMigrationFileTransactionItem transaction = CreateFileTransaction(
                        safetyRoot,
                        marker.PackageId,
                        targetRoot,
                        relative,
                        file.RelativePath);
                    state.Files.Add(transaction);
                    WriteState(safetyRoot, state);
                    PrepareFile(
                        stagingRoot,
                        file.RelativePath,
                        databaseSettings,
                        transaction);
                }

                return state;
            }
            catch
            {
                CleanupPrepared(state);
                CleanupSnapshots(state);
                AtomicFileHelper.TryDeleteFile(Path.Combine(safetyRoot, StateFileName));
                throw;
            }
        }

        public static void Apply(ServerMigrationFileTransactionState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (!state.FullMigration)
            {
                return;
            }

            foreach (ServerMigrationDirectoryTransaction directory in state.Directories)
            {
                EnsureNoReparsePoint(directory.TargetPath);
                AtomicFileHelper.TryDeleteDirectory(directory.OldSwapPath);
                if (Directory.Exists(directory.TargetPath))
                {
                    Directory.Move(directory.TargetPath, directory.OldSwapPath);
                }
                Directory.Move(directory.ReplacementPath, directory.TargetPath);
                RuntimeFilePermissionHelper.RestrictDirectory(directory.TargetPath);
                AtomicFileHelper.TryDeleteDirectory(directory.OldSwapPath);
            }

            foreach (ServerMigrationFileTransactionItem file in state.Files)
            {
                EnsureNoReparsePoint(file.TargetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
                AtomicFileHelper.WriteFileAtomic(
                    file.TargetPath,
                    temp => File.Copy(file.ReplacementPath, temp, overwrite: true));
                RuntimeFilePermissionHelper.RestrictFile(file.TargetPath);
            }
        }

        public static void Rollback(string safetyRoot)
        {
            ServerMigrationFileTransactionState state = ReadState(safetyRoot);
            if (state == null || !state.FullMigration)
            {
                return;
            }

            foreach (ServerMigrationDirectoryTransaction directory in state.Directories.AsEnumerable().Reverse())
            {
                AtomicFileHelper.TryDeleteDirectory(directory.TargetPath);
                AtomicFileHelper.TryDeleteDirectory(directory.OldSwapPath);
                if (directory.OriginallyExisted)
                {
                    CopyDirectory(directory.SnapshotPath, directory.TargetPath);
                }
            }

            foreach (ServerMigrationFileTransactionItem file in state.Files.AsEnumerable().Reverse())
            {
                if (file.OriginallyExisted)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
                    AtomicFileHelper.WriteFileAtomic(
                        file.TargetPath,
                        temp => File.Copy(file.SnapshotPath, temp, overwrite: true));
                    RuntimeFilePermissionHelper.RestrictFile(file.TargetPath);
                }
                else
                {
                    AtomicFileHelper.TryDeleteFile(file.TargetPath);
                }
            }

            CleanupPrepared(state);
        }

        public static void CleanupPrepared(ServerMigrationFileTransactionState state)
        {
            if (state == null)
            {
                return;
            }
            foreach (ServerMigrationDirectoryTransaction directory in state.Directories)
            {
                AtomicFileHelper.TryDeleteDirectory(directory.ReplacementPath);
                AtomicFileHelper.TryDeleteDirectory(directory.OldSwapPath);
            }
            foreach (ServerMigrationFileTransactionItem file in state.Files)
            {
                AtomicFileHelper.TryDeleteFile(file.ReplacementPath);
            }
        }

        internal static void CleanupSnapshots(ServerMigrationFileTransactionState state)
        {
            foreach (ServerMigrationDirectoryTransaction directory in state.Directories)
            {
                AtomicFileHelper.TryDeleteDirectory(directory.SnapshotPath);
            }

            foreach (ServerMigrationFileTransactionItem file in state.Files)
            {
                AtomicFileHelper.TryDeleteFile(file.SnapshotPath);
            }
        }

        public static ServerMigrationFileTransactionState ReadState(string safetyRoot)
        {
            string path = Path.Combine(safetyRoot, StateFileName);
            if (!File.Exists(path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<ServerMigrationFileTransactionState>(
                File.ReadAllText(path),
                ServerMigrationService.JsonOptions);
        }

        private static ServerMigrationDirectoryTransaction CreateDirectoryTransaction(
            string safetyRoot,
            PendingServerMigrationRestore marker,
            string targetRoot,
            string safetyName)
        {
            string fullTarget = Path.GetFullPath(targetRoot);
            EnsureNoReparsePoint(fullTarget);
            string parent = Path.GetDirectoryName(fullTarget)
                ?? throw new InvalidOperationException("服务器迁移数据目录缺少父目录。");
            string leaf = Path.GetFileName(fullTarget);
            string replacement = Path.Combine(parent, $".{leaf}.migration-new-{marker.PackageId}");
            string oldSwap = Path.Combine(parent, $".{leaf}.migration-old-{marker.PackageId}");
            string snapshot = Path.Combine(safetyRoot, "Data", safetyName);
            return new ServerMigrationDirectoryTransaction
            {
                TargetPath = fullTarget,
                ReplacementPath = replacement,
                OldSwapPath = oldSwap,
                SnapshotPath = snapshot,
                OriginallyExisted = Directory.Exists(fullTarget)
            };
        }

        private static void PrepareDirectory(
            string stagingRoot,
            PendingServerMigrationRestore marker,
            string entryPrefix,
            ServerMigrationDirectoryTransaction transaction)
        {
            try
            {
                AtomicFileHelper.TryDeleteDirectory(transaction.ReplacementPath);
                AtomicFileHelper.TryDeleteDirectory(transaction.OldSwapPath);
                AtomicFileHelper.TryDeleteDirectory(transaction.SnapshotPath);

                if (transaction.OriginallyExisted)
                {
                    CopyDirectory(transaction.TargetPath, transaction.SnapshotPath);
                }
                Directory.CreateDirectory(transaction.ReplacementPath);
                RuntimeFilePermissionHelper.RestrictDirectory(transaction.ReplacementPath);
                foreach (ServerMigrationFileManifest file in marker.Manifest.Files.Where(file =>
                    file.RelativePath.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    string relative = file.RelativePath[entryPrefix.Length..];
                    string source = ServerMigrationService.ResolvePath(stagingRoot, file.RelativePath);
                    string target = ResolveWithinRoot(transaction.ReplacementPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: false);
                    RuntimeFilePermissionHelper.RestrictFile(target);
                }
            }
            catch
            {
                AtomicFileHelper.TryDeleteDirectory(transaction.ReplacementPath);
                AtomicFileHelper.TryDeleteDirectory(transaction.OldSwapPath);
                AtomicFileHelper.TryDeleteDirectory(transaction.SnapshotPath);
                throw;
            }
        }

        private static ServerMigrationFileTransactionItem CreateFileTransaction(
            string safetyRoot,
            string packageId,
            string targetRoot,
            string relative,
            string entryName)
        {
            string target = ResolveWithinRoot(targetRoot, relative);
            EnsureNoReparsePoint(target);
            string snapshot = ResolveWithinRoot(
                Path.Combine(safetyRoot, "RuntimeFiles"),
                entryName);
            string replacement = Path.Combine(
                Path.GetDirectoryName(target)!,
                $".{Path.GetFileName(target)}.migration-new-{packageId}");
            return new ServerMigrationFileTransactionItem
            {
                TargetPath = target,
                ReplacementPath = replacement,
                SnapshotPath = snapshot,
                OriginallyExisted = File.Exists(target)
            };
        }

        private static void PrepareFile(
            string stagingRoot,
            string entryName,
            DatabaseConnectionSettings databaseSettings,
            ServerMigrationFileTransactionItem transaction)
        {
            string source = ServerMigrationService.ResolvePath(stagingRoot, entryName);
            try
            {
                if (transaction.OriginallyExisted)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(transaction.SnapshotPath)!);
                    File.Copy(transaction.TargetPath, transaction.SnapshotPath, overwrite: true);
                    RuntimeFilePermissionHelper.RestrictFile(transaction.SnapshotPath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(transaction.ReplacementPath)!);
                AtomicFileHelper.TryDeleteFile(transaction.ReplacementPath);
                if (entryName.Equals(
                    ServerMigrationLayout.ConfigEntry("appsettings.json"),
                    StringComparison.OrdinalIgnoreCase))
                {
                    WriteMergedAppSettings(source, transaction.ReplacementPath, databaseSettings);
                }
                else
                {
                    File.Copy(source, transaction.ReplacementPath, overwrite: false);
                }
                RuntimeFilePermissionHelper.RestrictFile(transaction.ReplacementPath);
            }
            catch
            {
                AtomicFileHelper.TryDeleteFile(transaction.ReplacementPath);
                AtomicFileHelper.TryDeleteFile(transaction.SnapshotPath);
                throw;
            }
        }

        internal static void WriteMergedAppSettings(
            string sourcePath,
            string targetPath,
            DatabaseConnectionSettings databaseSettings)
        {
            var root = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
                ?? throw new InvalidDataException("迁移包 appsettings.json 内容无效。");
            var system = root["System"] as JsonObject ?? new JsonObject();
            root["System"] = system;
            system["DatabaseProvider"] = DatabaseConnectionSettings.PostgreSqlProvider;
            system["PostgreSqlHost"] = databaseSettings.PostgreSqlHost;
            system["PostgreSqlPort"] = databaseSettings.PostgreSqlPort;
            system["PostgreSqlDatabase"] = databaseSettings.PostgreSqlDatabase;
            system["PostgreSqlUsername"] = databaseSettings.PostgreSqlUsername;
            system["PostgreSqlPassword"] = string.Empty;
            system["PostgreSqlAdditionalOptions"] = databaseSettings.PostgreSqlAdditionalOptions;
            AtomicFileHelper.WriteAllTextAtomic(
                targetPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void WriteState(string safetyRoot, ServerMigrationFileTransactionState state)
        {
            Directory.CreateDirectory(safetyRoot);
            RuntimeFilePermissionHelper.RestrictDirectory(safetyRoot);
            string path = Path.Combine(safetyRoot, StateFileName);
            AtomicFileHelper.WriteAllTextAtomic(
                path,
                JsonSerializer.Serialize(state, ServerMigrationService.JsonOptions));
            RuntimeFilePermissionHelper.RestrictFile(path);
        }

        private static void CopyDirectory(string sourceRoot, string targetRoot)
        {
            string fullSource = Path.GetFullPath(sourceRoot);
            if (!Directory.Exists(fullSource))
            {
                throw new DirectoryNotFoundException($"服务器迁移目录快照源不存在：{fullSource}");
            }
            if ((File.GetAttributes(fullSource) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"服务器迁移目录不能是符号链接或重解析点：{fullSource}");
            }
            string fullTarget = Path.GetFullPath(targetRoot);
            EnsureNoReparsePoint(fullTarget);
            Directory.CreateDirectory(fullTarget);
            RuntimeFilePermissionHelper.RestrictDirectory(fullTarget);
            var pending = new Stack<string>();
            pending.Push(fullSource);
            while (pending.Count > 0)
            {
                string currentSource = pending.Pop();
                foreach (FileSystemInfo item in new DirectoryInfo(currentSource).EnumerateFileSystemInfos())
                {
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"服务器迁移目录包含符号链接或重解析点：{item.FullName}");
                    }
                    string target = ResolveWithinRoot(
                        fullTarget,
                        Path.GetRelativePath(fullSource, item.FullName));
                    if (item is DirectoryInfo)
                    {
                        Directory.CreateDirectory(target);
                        RuntimeFilePermissionHelper.RestrictDirectory(target);
                        pending.Push(item.FullName);
                        continue;
                    }
                    if (item is not FileInfo)
                    {
                        throw new InvalidDataException(
                            $"服务器迁移目录包含不受支持的文件系统项：{item.FullName}");
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(item.FullName, target, overwrite: true);
                    RuntimeFilePermissionHelper.RestrictFile(target);
                }
            }
        }

        private static string ResolveWithinRoot(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root);
            string path = Path.GetFullPath(Path.Combine(
                fullRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathBoundaryHelper.IsWithinRoot(path, fullRoot))
            {
                throw new InvalidDataException("服务器迁移文件事务路径越界。");
            }
            return path;
        }

        private static void EnsureNoReparsePoint(string target)
        {
            string current = Path.GetFullPath(target);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileSystemInfo info = Directory.Exists(current)
                        ? new DirectoryInfo(current)
                        : new FileInfo(current);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"服务器迁移目标路径包含符号链接或重解析点：{current}");
                    }
                }
                current = Path.GetDirectoryName(current);
            }
        }
    }

    internal sealed class ServerMigrationFileTransactionState
    {
        public string PackageId { get; set; } = string.Empty;
        public bool FullMigration { get; set; }
        public List<ServerMigrationDirectoryTransaction> Directories { get; set; } = [];
        public List<ServerMigrationFileTransactionItem> Files { get; set; } = [];
    }

    internal sealed class ServerMigrationDirectoryTransaction
    {
        public string TargetPath { get; set; } = string.Empty;
        public string ReplacementPath { get; set; } = string.Empty;
        public string OldSwapPath { get; set; } = string.Empty;
        public string SnapshotPath { get; set; } = string.Empty;
        public bool OriginallyExisted { get; set; }
    }

    internal sealed class ServerMigrationFileTransactionItem
    {
        public string TargetPath { get; set; } = string.Empty;
        public string ReplacementPath { get; set; } = string.Empty;
        public string SnapshotPath { get; set; } = string.Empty;
        public bool OriginallyExisted { get; set; }
    }
}

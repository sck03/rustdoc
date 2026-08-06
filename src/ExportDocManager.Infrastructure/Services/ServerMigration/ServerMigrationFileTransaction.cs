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
            foreach ((string prefix, string targetRoot, string safetyName) in new[]
            {
                ("Data/Files/", paths.FileRoot, "Files"),
                ("Data/Templates/", paths.UserTemplateRoot, "Templates"),
                ("Data/SingleWindow/", paths.SingleWindowRoot, "SingleWindow"),
                ("Data/Marks/", Path.Combine(paths.DataRoot, "Marks"), "Marks")
            })
            {
                state.Directories.Add(PrepareDirectory(
                    stagingRoot,
                    safetyRoot,
                    marker,
                    prefix,
                    targetRoot,
                    safetyName));
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
                state.Files.Add(PrepareFile(
                    stagingRoot,
                    safetyRoot,
                    marker.PackageId,
                    file.RelativePath,
                    targetRoot,
                    relative,
                    databaseSettings));
            }

            WriteState(safetyRoot, state);
            return state;
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

        private static ServerMigrationDirectoryTransaction PrepareDirectory(
            string stagingRoot,
            string safetyRoot,
            PendingServerMigrationRestore marker,
            string entryPrefix,
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
            AtomicFileHelper.TryDeleteDirectory(replacement);
            AtomicFileHelper.TryDeleteDirectory(oldSwap);
            AtomicFileHelper.TryDeleteDirectory(snapshot);

            bool originallyExisted = Directory.Exists(fullTarget);
            if (originallyExisted)
            {
                CopyDirectory(fullTarget, snapshot);
            }
            Directory.CreateDirectory(replacement);
            RuntimeFilePermissionHelper.RestrictDirectory(replacement);
            foreach (ServerMigrationFileManifest file in marker.Manifest.Files.Where(file =>
                file.RelativePath.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                string relative = file.RelativePath[entryPrefix.Length..];
                string source = ServerMigrationService.ResolvePath(stagingRoot, file.RelativePath);
                string target = ResolveWithinRoot(replacement, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
                RuntimeFilePermissionHelper.RestrictFile(target);
            }

            return new ServerMigrationDirectoryTransaction
            {
                TargetPath = fullTarget,
                ReplacementPath = replacement,
                OldSwapPath = oldSwap,
                SnapshotPath = snapshot,
                OriginallyExisted = originallyExisted
            };
        }

        private static ServerMigrationFileTransactionItem PrepareFile(
            string stagingRoot,
            string safetyRoot,
            string packageId,
            string entryName,
            string targetRoot,
            string relative,
            DatabaseConnectionSettings databaseSettings)
        {
            string source = ServerMigrationService.ResolvePath(stagingRoot, entryName);
            string target = ResolveWithinRoot(targetRoot, relative);
            EnsureNoReparsePoint(target);
            string snapshot = ResolveWithinRoot(
                Path.Combine(safetyRoot, "RuntimeFiles"),
                entryName);
            bool originallyExisted = File.Exists(target);
            if (originallyExisted)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                File.Copy(target, snapshot, overwrite: true);
                RuntimeFilePermissionHelper.RestrictFile(snapshot);
            }

            string replacement = Path.Combine(
                Path.GetDirectoryName(target)!,
                $".{Path.GetFileName(target)}.migration-new-{packageId}");
            Directory.CreateDirectory(Path.GetDirectoryName(replacement)!);
            AtomicFileHelper.TryDeleteFile(replacement);
            if (entryName.Equals(
                ServerMigrationLayout.ConfigEntry("appsettings.json"),
                StringComparison.OrdinalIgnoreCase))
            {
                WriteMergedAppSettings(source, replacement, databaseSettings);
            }
            else
            {
                File.Copy(source, replacement, overwrite: false);
            }
            RuntimeFilePermissionHelper.RestrictFile(replacement);
            return new ServerMigrationFileTransactionItem
            {
                TargetPath = target,
                ReplacementPath = replacement,
                SnapshotPath = snapshot,
                OriginallyExisted = originallyExisted
            };
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

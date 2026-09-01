using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowClientProfilePathResolver
    {
        public static string GetBuiltInBusinessRoot(
            string singleWindowRoot,
            string profileKey,
            SingleWindowBusinessType businessType)
        {
            string normalizedProfileKey = profileKey?.Trim() ?? string.Empty;
            if (normalizedProfileKey.Length != 36 ||
                !normalizedProfileKey.StartsWith("SWP-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(normalizedProfileKey[4..], "N", out _))
            {
                throw new ArgumentException("操作档案标识无效。", nameof(profileKey));
            }
            string clientRoot = Path.Combine(
                (singleWindowRoot ?? string.Empty).Trim(),
                "Client",
                "Profiles",
                normalizedProfileKey);
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => Path.Combine(clientRoot, "CustomsCoo"),
                SingleWindowBusinessType.AgentConsignment => Path.Combine(clientRoot, "AgentConsignment"),
                _ => throw new ArgumentOutOfRangeException(nameof(businessType))
            };
        }

        public static string ResolveConfiguredRoot(
            SwClientProfile profile,
            SingleWindowBusinessType businessType)
        {
            ArgumentNullException.ThrowIfNull(profile);
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => profile.CustomsCooClientRootPath ?? string.Empty,
                SingleWindowBusinessType.AgentConsignment => profile.AgentConsignmentClientRootPath ?? string.Empty,
                _ => string.Empty
            };
        }

        public static string NormalizeClientRootPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return string.Empty;
            }

            string trimmed = rootPath.Trim();
            if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ServiceValidationException("持卡机官方客户端目录必须位于本机磁盘，不能使用网络共享路径。");
            }

            if (!Path.IsPathRooted(trimmed))
            {
                throw new ServiceValidationException("持卡机官方客户端目录必须使用本机绝对路径。");
            }

            string normalized = Path.GetFullPath(trimmed);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                normalized,
                "官方单一窗口客户端目录不能经过符号链接、目录联接或其他重解析点。");
            string leafName = Path.GetFileName(
                normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(leafName, "OutBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(leafName, "SentBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(leafName, "InBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(leafName, "FailBox", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Directory.GetParent(normalized)?.FullName ?? normalized;
            }

            string pathRoot = Path.GetPathRoot(normalized) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(pathRoot) &&
                string.Equals(
                    Path.GetFullPath(pathRoot),
                    Path.GetFullPath(normalized),
                    PathBoundaryHelper.PathComparison))
            {
                throw new ServiceValidationException("持卡机官方客户端目录不能直接使用磁盘根目录，请选择专用子目录。");
            }

            return normalized;
        }

        /// <summary>
        /// Validates and, when requested, prepares the official client's business
        /// directory.  All callers use this one gate so a configured root cannot be
        /// treated as safe merely because a different operation happened to create it
        /// first.  Existing ancestors and the created tree are checked for links and
        /// reparse points before a write probe is attempted.
        /// </summary>
        public static string EnsureClientRoot(
            string rootPath,
            bool createDirectories,
            bool requireWritable = false)
        {
            string normalizedRoot = NormalizeClientRootPath(rootPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                throw new ServiceValidationException("本机操作卡尚未配置官方单一窗口客户端目录。");
            }

            const string errorMessage = "官方单一窗口客户端目录无效或包含符号链接、目录联接及其他重解析点。";
            PathBoundaryHelper.EnsureNoLinkLikeComponents(normalizedRoot, errorMessage);
            EnsureDirectoryState(normalizedRoot, createDirectories);

            if (createDirectories)
            {
                Directory.CreateDirectory(normalizedRoot);
            }

            PathBoundaryHelper.EnsureNoLinkLikeComponents(normalizedRoot, errorMessage);
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                normalizedRoot,
                normalizedRoot,
                errorMessage);

            if (createDirectories)
            {
                foreach (string folderName in StandardFolderNames)
                {
                    string folderPath = Path.Combine(normalizedRoot, folderName);
                    PathBoundaryHelper.EnsureNoLinkLikeComponents(folderPath, errorMessage);
                    Directory.CreateDirectory(folderPath);
                    PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                        folderPath,
                        normalizedRoot,
                        errorMessage);
                }
            }

            if (requireWritable)
            {
                ProbeWritable(
                    Path.Combine(normalizedRoot, "OutBox"),
                    errorMessage);
            }

            return normalizedRoot;
        }

        private static void EnsureDirectoryState(string path, bool createDirectories)
        {
            if (Directory.Exists(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                throw new ServiceValidationException("官方单一窗口客户端目录必须是目录，不能指向普通文件。");
            }

            try
            {
                _ = File.GetAttributes(path);
                throw new ServiceValidationException("官方单一窗口客户端目录不是可用目录。");
            }
            catch (FileNotFoundException)
            {
                if (!createDirectories)
                {
                    return;
                }
            }
            catch (DirectoryNotFoundException)
            {
                if (!createDirectories)
                {
                    return;
                }
            }
        }

        private static void ProbeWritable(string directory, string errorMessage)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(directory, errorMessage);
            string probePath = Path.Combine(directory, $".exportdoc-write-probe-{Guid.NewGuid():N}.tmp");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(probePath, errorMessage);
            try
            {
                using var stream = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.WriteThrough | FileOptions.DeleteOnClose);
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException)
            {
                throw new IOException(errorMessage, ex);
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(probePath);
            }
        }

        private static readonly string[] StandardFolderNames =
        [
            "OutBox",
            "SentBox",
            "InBox",
            "FailBox"
        ];
    }
}

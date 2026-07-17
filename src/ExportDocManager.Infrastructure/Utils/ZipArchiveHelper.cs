using System.IO.Compression;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Utils
{
    public static class ZipArchiveHelper
    {
        public static async Task CreateFromDirectoryAsync(
            string sourceDirectory,
            string zipPath,
            CancellationToken cancellationToken = default,
            IProgress<OperationProgressUpdate> progress = null,
            string statusText = "正在生成压缩包",
            int startPercent = 0,
            int endPercent = 100)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

            var fullSourceDirectory = Path.GetFullPath(sourceDirectory);
            if (!Directory.Exists(fullSourceDirectory))
            {
                throw new DirectoryNotFoundException($"目录不存在：{fullSourceDirectory}");
            }

            var entries = Directory
                .GetFiles(fullSourceDirectory, "*", SearchOption.AllDirectories)
                .Select(file => (
                    SourcePath: file,
                    EntryName: Path.GetRelativePath(fullSourceDirectory, file)));

            await CreateFromFilesAsync(
                entries,
                zipPath,
                cancellationToken,
                progress,
                statusText,
                startPercent,
                endPercent,
                "当前没有需要压缩的文件。");
        }

        public static async Task CreateFromFilesAsync(
            IEnumerable<(string SourcePath, string EntryName)> entries,
            string zipPath,
            CancellationToken cancellationToken = default,
            IProgress<OperationProgressUpdate> progress = null,
            string statusText = "正在生成压缩包",
            int startPercent = 0,
            int endPercent = 100,
            string emptyDetailText = "当前没有需要压缩的文件。")
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

            var normalizedEntries = entries
                .Select(entry => (
                    SourcePath: Path.GetFullPath(entry.SourcePath),
                    EntryName: NormalizeEntryName(entry.EntryName)))
                .OrderBy(entry => entry.EntryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var duplicateEntryName = normalizedEntries
                .GroupBy(entry => entry.EntryName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
            if (!string.IsNullOrWhiteSpace(duplicateEntryName))
            {
                throw new InvalidDataException($"压缩包条目重复：{duplicateEntryName}");
            }

            foreach (var entry in normalizedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(entry.SourcePath))
                {
                    throw new FileNotFoundException("待打包文件不存在。", entry.SourcePath);
                }
            }

            await AtomicFileHelper.WriteFileAtomicAsync(
                zipPath,
                (tempZipPath, ct) => WriteZipFileAsync(
                    normalizedEntries,
                    tempZipPath,
                    ct,
                    progress,
                    statusText,
                    startPercent,
                    endPercent,
                    emptyDetailText),
                cancellationToken);
        }

        public static async Task ExtractToDirectorySafeAsync(
            string packagePath,
            string targetDirectory,
            CancellationToken cancellationToken = default,
            IProgress<OperationProgressUpdate> progress = null,
            string statusText = "正在解压压缩包",
            int startPercent = 0,
            int endPercent = 100)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

            var fullPackagePath = Path.GetFullPath(packagePath);
            if (!File.Exists(fullPackagePath))
            {
                throw new FileNotFoundException("压缩包不存在。", fullPackagePath);
            }

            var fullTargetDirectory = Path.GetFullPath(targetDirectory);
            Directory.CreateDirectory(fullTargetDirectory);

            using var archive = ZipFile.OpenRead(fullPackagePath);
            var fileEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToList();

            if (fileEntries.Count == 0)
            {
                ReportProgress(progress, statusText, "压缩包中没有可解压的文件。", endPercent);
                return;
            }

            foreach (var entry in archive.Entries.Where(entry => string.IsNullOrWhiteSpace(entry.Name)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directoryPath = Path.GetFullPath(Path.Combine(fullTargetDirectory, entry.FullName));
                EnsurePathWithinRoot(directoryPath, fullTargetDirectory);
                Directory.CreateDirectory(directoryPath);
            }

            for (var index = 0; index < fileEntries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = fileEntries[index];
                var targetPath = Path.GetFullPath(Path.Combine(fullTargetDirectory, entry.FullName));
                EnsurePathWithinRoot(targetPath, fullTargetDirectory);
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await AtomicFileHelper.WriteFileAtomicAsync(
                    targetPath,
                    async (tempTargetPath, ct) =>
                    {
                        await using var sourceStream = entry.Open();
                        await using var targetStream = new FileStream(
                            tempTargetPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await sourceStream.CopyToAsync(targetStream, 81920, ct);
                    },
                    cancellationToken);
                ReportProgress(
                    progress,
                    statusText,
                    $"已解压：{entry.FullName}",
                    CalculateProgress(index + 1, fileEntries.Count, startPercent, endPercent));
            }
        }

        private static async Task WriteZipFileAsync(
            IReadOnlyList<(string SourcePath, string EntryName)> entries,
            string zipPath,
            CancellationToken cancellationToken,
            IProgress<OperationProgressUpdate> progress,
            string statusText,
            int startPercent,
            int endPercent,
            string emptyDetailText)
        {
            await using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

            if (entries.Count == 0)
            {
                ReportProgress(progress, statusText, emptyDetailText, endPercent);
                return;
            }

            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = entries[index];
                var zipEntry = archive.CreateEntry(source.EntryName, CompressionLevel.Optimal);
                await using var sourceStream = new FileStream(
                    source.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var entryStream = zipEntry.Open();
                await sourceStream.CopyToAsync(entryStream, 81920, cancellationToken);
                ReportProgress(
                    progress,
                    statusText,
                    $"已打包：{source.EntryName}",
                    CalculateProgress(index + 1, entries.Count, startPercent, endPercent));
            }
        }

        private static void EnsurePathWithinRoot(string candidatePath, string rootPath)
        {
            var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedCandidate = candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                !normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("压缩包内含有无效路径。");
            }
        }

        private static string NormalizeEntryName(string entryName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

            if (Path.IsPathRooted(entryName))
            {
                throw new InvalidDataException("压缩包条目不能使用绝对路径。");
            }

            var normalized = entryName.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.Split('/').Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    segment.Equals(".", StringComparison.Ordinal) ||
                    segment.Equals("..", StringComparison.Ordinal)))
            {
                throw new InvalidDataException("压缩包条目路径无效。");
            }

            return normalized;
        }

        private static int CalculateProgress(int current, int total, int startPercent, int endPercent)
        {
            if (total <= 0)
            {
                return endPercent;
            }

            var clampedStart = Math.Clamp(startPercent, 0, 100);
            var clampedEnd = Math.Clamp(endPercent, clampedStart, 100);
            if (current <= 0)
            {
                return clampedStart;
            }

            var ratio = (double)current / total;
            return clampedStart + (int)Math.Round((clampedEnd - clampedStart) * ratio);
        }

        private static void ReportProgress(
            IProgress<OperationProgressUpdate> progress,
            string statusText,
            string detailText,
            int? percent = null)
        {
            progress?.Report(new OperationProgressUpdate
            {
                StatusText = statusText ?? string.Empty,
                DetailText = detailText ?? string.Empty,
                ProgressPercent = percent
            });
        }
    }
}

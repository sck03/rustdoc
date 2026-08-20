using System.IO.Compression;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Utils
{
    public sealed record ZipExtractionLimits(
        int MaximumEntries = 5_000,
        long MaximumEntryBytes = 128L * 1024L * 1024L,
        long MaximumTotalBytes = 512L * 1024L * 1024L,
        double MaximumCompressionRatio = 500d,
        int MaximumPathDepth = 32)
    {
        public static ZipExtractionLimits Default { get; } = new();
    }

    public static class ZipArchiveHelper
    {
        public static async Task CreateFromDirectoryAsync(
            string sourceDirectory,
            string zipPath,
            CancellationToken cancellationToken = default,
            IProgress<OperationProgressUpdate>? progress = null,
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
            IProgress<OperationProgressUpdate>? progress = null,
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
                .ThenBy(entry => entry.SourcePath, PhysicalPathComparison.Comparer)
                .ToList();

            var duplicateEntryName = normalizedEntries
                .GroupBy(entry => entry.EntryName, PortablePathKey.Comparer)
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
            IProgress<OperationProgressUpdate>? progress = null,
            string statusText = "正在解压压缩包",
            int startPercent = 0,
            int endPercent = 100,
            ZipExtractionLimits? limits = null)
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
            limits ??= ZipExtractionLimits.Default;
            ValidateLimits(limits);

            using var archive = ZipFile.OpenRead(fullPackagePath);
            if (archive.Entries.Count > limits.MaximumEntries)
            {
                throw new InvalidDataException($"压缩包条目数超过允许上限 {limits.MaximumEntries}。");
            }

            var normalizedNames = new Dictionary<ZipArchiveEntry, string>();
            var uniqueNames = new HashSet<string>(PortablePathKey.Comparer);
            long declaredTotalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                string normalizedName = NormalizeArchiveEntryName(entry.FullName, limits.MaximumPathDepth);
                if (!uniqueNames.Add(normalizedName))
                {
                    throw new InvalidDataException($"压缩包包含重复条目：{normalizedName}");
                }

                ValidateEntryResourceBudget(entry, limits);
                if (!string.IsNullOrWhiteSpace(entry.Name))
                {
                    if (declaredTotalBytes > limits.MaximumTotalBytes - entry.Length)
                    {
                        throw new InvalidDataException("压缩包声明的展开总大小超过允许上限。");
                    }
                    declaredTotalBytes += entry.Length;
                }
                normalizedNames[entry] = normalizedName;
            }

            var fileEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToList();

            if (fileEntries.Count == 0)
            {
                OperationProgressReporter.Report(progress, statusText, "压缩包中没有可解压的文件。", endPercent);
                return;
            }

            foreach (var entry in archive.Entries.Where(entry => string.IsNullOrWhiteSpace(entry.Name)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directoryPath = Path.GetFullPath(Path.Combine(fullTargetDirectory, normalizedNames[entry]));
                EnsurePathWithinRoot(directoryPath, fullTargetDirectory);
                Directory.CreateDirectory(directoryPath);
            }

            long extractedTotalBytes = 0;
            for (var index = 0; index < fileEntries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = fileEntries[index];
                var targetPath = Path.GetFullPath(Path.Combine(fullTargetDirectory, normalizedNames[entry]));
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
                        long remainingTotal = limits.MaximumTotalBytes - extractedTotalBytes;
                        long entryBudget = Math.Min(limits.MaximumEntryBytes, remainingTotal);
                        try
                        {
                            long extractedBytes = await BoundedStreamHelper.CopyToAsync(
                                sourceStream,
                                targetStream,
                                entryBudget,
                                ct);
                            extractedTotalBytes += extractedBytes;
                        }
                        catch (PayloadLimitExceededException ex)
                        {
                            throw new InvalidDataException(
                                $"压缩包条目 {entry.FullName} 展开后超过资源配额。",
                                ex);
                        }
                    },
                    cancellationToken);
                OperationProgressReporter.Report(
                    progress,
                    statusText,
                    $"已解压：{entry.FullName}",
                    OperationProgressReporter.Calculate(index + 1, fileEntries.Count, startPercent, endPercent));
            }
        }

        private static void ValidateLimits(ZipExtractionLimits limits)
        {
            if (limits.MaximumEntries <= 0 ||
                limits.MaximumEntryBytes <= 0 ||
                limits.MaximumTotalBytes <= 0 ||
                limits.MaximumEntryBytes > limits.MaximumTotalBytes ||
                limits.MaximumCompressionRatio < 1 ||
                limits.MaximumPathDepth <= 0)
            {
                throw new ArgumentException("ZIP 解压资源配额无效。", nameof(limits));
            }
        }

        private static void ValidateEntryResourceBudget(ZipArchiveEntry entry, ZipExtractionLimits limits)
        {
            if (entry.Length < 0 || entry.CompressedLength < 0)
            {
                throw new InvalidDataException($"压缩包条目大小无效：{entry.FullName}");
            }
            if (entry.Length > limits.MaximumEntryBytes)
            {
                throw new InvalidDataException($"压缩包条目 {entry.FullName} 展开后超过单条目上限。");
            }

            if (entry.Length > 1024L * 1024L)
            {
                double ratio = entry.CompressedLength == 0
                    ? double.PositiveInfinity
                    : (double)entry.Length / entry.CompressedLength;
                if (ratio > limits.MaximumCompressionRatio)
                {
                    throw new InvalidDataException($"压缩包条目 {entry.FullName} 的压缩比异常。");
                }
            }
        }

        private static string NormalizeArchiveEntryName(string entryName, int maximumPathDepth)
        {
            string normalized = NormalizeEntryName(entryName);
            if (normalized.Split('/').Length > maximumPathDepth)
            {
                throw new InvalidDataException($"压缩包条目目录层级超过允许上限：{entryName}");
            }
            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }

        private static async Task WriteZipFileAsync(
            IReadOnlyList<(string SourcePath, string EntryName)> entries,
            string zipPath,
            CancellationToken cancellationToken,
            IProgress<OperationProgressUpdate>? progress,
            string statusText,
            int startPercent,
            int endPercent,
            string emptyDetailText)
        {
            await using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

            if (entries.Count == 0)
            {
                OperationProgressReporter.Report(progress, statusText, emptyDetailText, endPercent);
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
                OperationProgressReporter.Report(
                    progress,
                    statusText,
                    $"已打包：{source.EntryName}",
                    OperationProgressReporter.Calculate(index + 1, entries.Count, startPercent, endPercent));
            }
        }

        private static void EnsurePathWithinRoot(string candidatePath, string rootPath)
        {
            if (!PathBoundaryHelper.IsWithinRoot(candidatePath, rootPath))
            {
                throw new InvalidDataException("压缩包内含有无效路径。");
            }
        }

        private static string NormalizeEntryName(string entryName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

            try
            {
                return PortablePathKey.NormalizeRelativePath(entryName);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException("压缩包条目路径无效。", ex);
            }
        }

    }
}

using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private static async Task CopyFilesAsync(
            IReadOnlyList<string> sourceFiles,
            string sourceRoot,
            string targetRoot,
            bool overwrite,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken,
            string statusText,
            int startPercent,
            int endPercent)
        {
            string fullSourceRoot = Path.GetFullPath(sourceRoot);
            string fullTargetRoot = Path.GetFullPath(targetRoot);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullSourceRoot,
                "模板包源目录不能经过符号链接、目录联接或其他重解析点。");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullTargetRoot,
                "模板目标目录不能经过符号链接、目录联接或其他重解析点。");
            Directory.CreateDirectory(fullTargetRoot);
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                fullTargetRoot,
                fullTargetRoot,
                "模板目标目录不能包含符号链接、目录联接或其他重解析点。");

            var files = sourceFiles ?? Array.Empty<string>();
            if (files.Count == 0)
            {
                OperationProgressReporter.Report(progress, statusText, "当前没有需要复制的文件。", endPercent);
                return;
            }

            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string file = PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    files[index],
                    fullSourceRoot,
                    "模板包源文件必须位于受管目录且不能经过链接类路径。");
                string relativePath = PortablePathKey.NormalizeRelativePath(
                    Path.GetRelativePath(fullSourceRoot, file));
                string targetFile = PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    Path.Combine(fullTargetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                    fullTargetRoot,
                    "模板目标文件必须位于受管目录且不能经过链接类路径。");
                string? targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                        targetDirectory,
                        fullTargetRoot,
                        "模板目标子目录不能包含符号链接、目录联接或其他重解析点。");
                }

                if (!overwrite && File.Exists(targetFile))
                {
                    OperationProgressReporter.Report(
                        progress,
                        statusText,
                        $"已跳过现有文件：{relativePath}",
                        OperationProgressReporter.Calculate(index + 1, files.Count, startPercent, endPercent));
                    continue;
                }

                try
                {
                    await FileCopyHelper.CopyAsync(
                        file,
                        targetFile,
                        overwrite,
                        cancellationToken).ConfigureAwait(false);
                    OperationProgressReporter.Report(
                        progress,
                        statusText,
                        $"正在处理：{relativePath}",
                        OperationProgressReporter.Calculate(index + 1, files.Count, startPercent, endPercent));
                }
                catch (FileNotFoundException ex)
                {
                    throw new InvalidDataException($"模板包复制源文件在操作期间消失：{relativePath}", ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new InvalidDataException($"模板包复制源目录在操作期间消失：{relativePath}", ex);
                }
            }
        }

        private static async Task<TemplateFileManifestSummary> BuildFileManifestAsync(
            string root,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> files = ControlledFileSystemEnumerator.EnumerateFiles(root, cancellationToken);
            var entries = new List<TemplateFileManifest>(files.Count);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    file,
                    root,
                    "模板文件清单不能经过符号链接、目录联接或其他重解析点。");
                string relativePath = PortablePathKey.NormalizeRelativePath(Path.GetRelativePath(root, file));
                var info = new FileInfo(file);
                if (!info.Exists || info.Length < 0)
                {
                    throw new InvalidDataException($"模板文件在生成清单时不可用：{relativePath}");
                }

                await using var stream = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                string sha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                    .ToLowerInvariant();
                entries.Add(new TemplateFileManifest(relativePath, info.Length, sha256));
            }

            var ordered = entries
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            long totalBytes = ordered.Aggregate(0L, (total, entry) => checked(total + entry.SizeBytes));
            return new TemplateFileManifestSummary(
                ordered,
                ordered.Count,
                totalBytes,
                ComputeFilesDigest(ordered));
        }

        private static async Task ValidateFileManifestAsync(
            string root,
            IReadOnlyList<string> sourceFiles,
            TemplatePackageManifest manifest,
            CancellationToken cancellationToken)
        {
            if (manifest.Files == null || manifest.FileCount < 0 || manifest.TotalBytes < 0 ||
                string.IsNullOrWhiteSpace(manifest.FilesDigest))
            {
                throw new InvalidDataException("模板包 config.json 缺少完整的文件摘要清单。");
            }

            var actual = await BuildFileManifestAsync(root, cancellationToken).ConfigureAwait(false);
            if (actual.FileCount != manifest.FileCount ||
                actual.TotalBytes != manifest.TotalBytes ||
                !string.Equals(actual.FilesDigest, manifest.FilesDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("模板包文件数量、大小或摘要与 config.json 不一致。");
            }

            var expectedByPath = manifest.Files.ToDictionary(
                entry => PortablePathKey.NormalizeRelativePath(entry.Path),
                PortablePathKey.Comparer);
            if (expectedByPath.Count != manifest.Files.Count)
            {
                throw new InvalidDataException("模板包文件清单包含重复或冲突路径。");
            }

            foreach (var entry in actual.Files)
            {
                if (!expectedByPath.TryGetValue(entry.Path, out var expected) ||
                    expected.SizeBytes != entry.SizeBytes ||
                    !string.Equals(expected.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"模板包文件摘要不匹配：{entry.Path}");
                }
            }

            var actualPaths = actual.Files.Select(entry => entry.Path).ToHashSet(PortablePathKey.Comparer);
            if (expectedByPath.Keys.Any(path => !actualPaths.Contains(path)))
            {
                throw new InvalidDataException("模板包清单包含缺失文件。");
            }

            // sourceFiles is supplied by the caller after its policy filtering; ensure no
            // untracked payload slipped through under a different extension or casing.
            var suppliedPaths = sourceFiles
                .Select(path => PortablePathKey.NormalizeRelativePath(Path.GetRelativePath(root, path)))
                .ToHashSet(PortablePathKey.Comparer);
            if (!suppliedPaths.SetEquals(actualPaths))
            {
                throw new InvalidDataException("模板包存在未经过清单校验的文件。");
            }
        }

        private static string ComputeFilesDigest(IEnumerable<TemplateFileManifest> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(entry.Path).Append('\n')
                    .Append(entry.SizeBytes).Append('\n')
                    .Append(entry.Sha256).Append('\n');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
                .ToLowerInvariant();
        }
    }
}

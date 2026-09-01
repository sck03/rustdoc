namespace ExportDocManager.Utils
{
    public static class FileCopyHelper
    {
        private const int BufferSize = 81920;

        public static void Copy(
            string sourcePath,
            string targetPath,
            bool overwrite,
            FileShare sourceFileShare = FileShare.Read)
        {
            var copyPlan = PrepareCopy(sourcePath, targetPath);
            string temporaryPath = AtomicFileHelper.GetSiblingTempFilePath(copyPlan.FullTargetPath);
            try
            {
                using var sourceStream = new FileStream(
                    copyPlan.FullSourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    sourceFileShare,
                    BufferSize,
                    FileOptions.SequentialScan);
                using (var targetStream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    sourceStream.CopyTo(targetStream, BufferSize);
                    targetStream.Flush(flushToDisk: true);
                }

                // A no-overwrite copy must make the existence check and rename one
                // filesystem operation.  Checking first and then calling the
                // replacement helper leaves a TOCTOU window in which another process
                // can create the destination and have it overwritten.
                if (overwrite)
                {
                    AtomicFileHelper.ReplaceFile(temporaryPath, copyPlan.FullTargetPath);
                }
                else
                {
                    File.Move(temporaryPath, copyPlan.FullTargetPath, overwrite: false);
                }
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(temporaryPath);
            }
        }

        public static async Task CopyAsync(
            string sourcePath,
            string targetPath,
            bool overwrite,
            CancellationToken cancellationToken = default,
            FileShare sourceFileShare = FileShare.Read)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copyPlan = PrepareCopy(sourcePath, targetPath);
            string temporaryPath = AtomicFileHelper.GetSiblingTempFilePath(copyPlan.FullTargetPath);
            try
            {
                // Keep every operation that can fail after the sibling temp path is
                // reserved inside the cleanup scope.  In particular, opening the
                // source can fail because another process holds an exclusive share,
                // and cancellation may arrive between preparation and I/O.
                await using var sourceStream = new FileStream(
                    copyPlan.FullSourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    sourceFileShare,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                cancellationToken.ThrowIfCancellationRequested();

                await using (var targetStream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    await sourceStream.CopyToAsync(targetStream, BufferSize, cancellationToken).ConfigureAwait(false);
                    await targetStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    targetStream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (overwrite)
                {
                    await AtomicFileHelper.ReplaceFileAsync(
                        temporaryPath,
                        copyPlan.FullTargetPath,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // File.Move(..., overwrite: false) is the atomic no-replace
                    // primitive.  It intentionally remains synchronous because only
                    // the directory entry operation is performed here; the bytes
                    // were already flushed asynchronously above.
                    File.Move(temporaryPath, copyPlan.FullTargetPath, overwrite: false);
                }
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(temporaryPath);
            }
        }

        private static (string FullSourcePath, string FullTargetPath) PrepareCopy(
            string sourcePath,
            string targetPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullTargetPath = Path.GetFullPath(targetPath);
            if (PhysicalPathComparison.Comparer.Equals(fullSourcePath, fullTargetPath))
            {
                throw new ArgumentException("源文件和目标文件不能相同。", nameof(targetPath));
            }
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullSourcePath,
                "复制源文件不能经过符号链接、目录联接或其他重解析点。");
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullTargetPath,
                "复制目标文件不能经过符号链接、目录联接或其他重解析点。");
            if (!File.Exists(fullSourcePath))
            {
                throw new FileNotFoundException("待复制文件不存在。", fullSourcePath);
            }

            string? targetDirectory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    targetDirectory,
                    "复制目标目录不能经过符号链接、目录联接或其他重解析点。");
                Directory.CreateDirectory(targetDirectory);
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    targetDirectory,
                    "复制目标目录不能经过符号链接、目录联接或其他重解析点。");
            }

            return (fullSourcePath, fullTargetPath);
        }
    }
}

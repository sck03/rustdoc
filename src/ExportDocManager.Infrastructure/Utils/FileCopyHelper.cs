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
            var copyPlan = PrepareCopy(sourcePath, targetPath, overwrite);

            using var sourceStream = new FileStream(
                copyPlan.FullSourcePath,
                FileMode.Open,
                FileAccess.Read,
                sourceFileShare,
                BufferSize,
                FileOptions.SequentialScan);
            using var targetStream = new FileStream(
                copyPlan.FullTargetPath,
                copyPlan.TargetFileMode,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.SequentialScan);

            sourceStream.CopyTo(targetStream, BufferSize);
        }

        public static async Task CopyAsync(
            string sourcePath,
            string targetPath,
            bool overwrite,
            CancellationToken cancellationToken = default,
            FileShare sourceFileShare = FileShare.Read)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copyPlan = PrepareCopy(sourcePath, targetPath, overwrite);
            await using var sourceStream = new FileStream(
                copyPlan.FullSourcePath,
                FileMode.Open,
                FileAccess.Read,
                sourceFileShare,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            cancellationToken.ThrowIfCancellationRequested();

            await using var targetStream = new FileStream(
                copyPlan.FullTargetPath,
                copyPlan.TargetFileMode,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await sourceStream.CopyToAsync(targetStream, BufferSize, cancellationToken);
        }

        private static (string FullSourcePath, string FullTargetPath, FileMode TargetFileMode) PrepareCopy(
            string sourcePath,
            string targetPath,
            bool overwrite)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullTargetPath = Path.GetFullPath(targetPath);
            if (!File.Exists(fullSourcePath))
            {
                throw new FileNotFoundException("待复制文件不存在。", fullSourcePath);
            }

            string targetDirectory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var targetFileMode = overwrite ? FileMode.Create : FileMode.CreateNew;
            return (fullSourcePath, fullTargetPath, targetFileMode);
        }
    }
}

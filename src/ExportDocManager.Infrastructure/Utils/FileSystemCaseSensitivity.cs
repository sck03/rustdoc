namespace ExportDocManager.Utils
{
    public static class FileSystemCaseSensitivity
    {
        public static bool IsCaseSensitive(string directoryPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

            string directory = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"无法检测文件系统大小写能力，目录不存在：{directory}");
            }

            string token = Guid.NewGuid().ToString("N");
            string probePath = Path.Combine(directory, $".edm-case-probe-{token}-a");
            string alternatePath = Path.Combine(directory, $".edm-case-probe-{token}-A");
            try
            {
                using (new FileStream(
                           probePath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.ReadWrite | FileShare.Delete,
                           bufferSize: 1,
                           FileOptions.None))
                {
                    return !File.Exists(alternatePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"无法检测运行数据卷的大小写能力：{directory}",
                    ex);
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(probePath);
                AtomicFileHelper.TryDeleteFile(alternatePath);
            }
        }
    }
}

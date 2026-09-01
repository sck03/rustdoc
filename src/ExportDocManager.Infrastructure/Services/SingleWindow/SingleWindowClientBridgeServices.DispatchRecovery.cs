namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        private static void EnsureWorkingDirectoryRemoved(string restoredDirectory)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(restoredDirectory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "单一窗口提交包恢复目录清理后仍包含符号链接或其他重解析点。");
                }

                throw new IOException("单一窗口提交包恢复目录清理失败，已停止重新解包。");
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // The directory was removed successfully.
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (IOException exception)
            {
                throw new InvalidDataException(
                    "单一窗口提交包恢复目录清理失败，已停止重新解包。",
                    exception);
            }
        }
    }
}

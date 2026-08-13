namespace ExportDocManager.Utils
{
    public static class LocalFileUriHelper
    {
        public static string FromPath(string localPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

            string fullPath = Path.GetFullPath(localPath.Trim());
            if (OperatingSystem.IsWindows())
            {
                fullPath = RemoveWindowsExtendedPathPrefix(fullPath);
            }

            return new Uri(fullPath).AbsoluteUri;
        }

        private static string RemoveWindowsExtendedPathPrefix(string localPath)
        {
            const string extendedPathPrefix = @"\\?\";
            const string extendedUncPrefix = @"\\?\UNC\";

            if (localPath.StartsWith(extendedUncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + localPath[extendedUncPrefix.Length..];
            }

            return localPath.StartsWith(extendedPathPrefix, StringComparison.OrdinalIgnoreCase)
                ? localPath[extendedPathPrefix.Length..]
                : localPath;
        }
    }
}

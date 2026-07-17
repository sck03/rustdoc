using System;
using System.IO;

namespace ExportDocManager.Utils
{
    internal static class PackagePathHelper
    {
        public static string NormalizePackagePath(string path, string extension, string argumentName = "path")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(extension);

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("保存路径不能为空。", argumentName);
            }

            var normalizedExtension = extension.StartsWith('.')
                ? extension
                : "." + extension;
            var normalizedPath = path.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, normalizedExtension.TrimStart('.'));
            var directory = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return normalizedPath;
        }
    }
}

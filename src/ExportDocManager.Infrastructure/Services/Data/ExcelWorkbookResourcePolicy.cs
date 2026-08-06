using System.IO.Compression;

namespace ExportDocManager.Services.Data
{
    internal sealed record ExcelWorkbookResourceLimits(
        long MaximumPackageBytes = 25L * 1024L * 1024L,
        int MaximumEntries = 2_048,
        long MaximumEntryBytes = 64L * 1024L * 1024L,
        long MaximumTotalExpandedBytes = 256L * 1024L * 1024L,
        double MaximumCompressionRatio = 500d);

    public static class ExcelWorkbookResourcePolicy
    {
        private static readonly ExcelWorkbookResourceLimits DefaultLimits = new();

        public static void Validate(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            if (!IsOpenXmlWorkbook(filePath))
            {
                return;
            }

            string normalizedPath = Path.GetFullPath(filePath);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("Excel 工作簿不存在。", normalizedPath);
            }

            using var input = new FileStream(
                normalizedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.SequentialScan);
            ValidateOpenXmlPackage(input, DefaultLimits);
        }

        public static void Validate(Stream input, string fileName)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (IsOpenXmlWorkbook(fileName))
            {
                ValidateOpenXmlPackage(input, DefaultLimits);
            }
        }

        public static void ValidateOpenXmlPackage(Stream input)
        {
            ValidateOpenXmlPackage(input, DefaultLimits);
        }

        internal static void ValidateOpenXmlPackage(
            Stream input,
            ExcelWorkbookResourceLimits limits)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(limits);
            ValidateLimits(limits);
            if (!input.CanRead || !input.CanSeek)
            {
                throw new InvalidDataException("Excel 工作簿输入流必须支持读取和定位。");
            }

            long originalPosition = input.Position;
            try
            {
                long packageBytes = input.Length;
                if (packageBytes <= 0)
                {
                    throw new InvalidDataException("Excel 工作簿不能为空。");
                }
                if (packageBytes > limits.MaximumPackageBytes)
                {
                    throw new InvalidDataException(
                        $"Excel 工作簿大小超过 {limits.MaximumPackageBytes / (1024 * 1024)} MB 上限。");
                }

                input.Position = 0;
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException("Excel 工作簿压缩包不包含任何条目。");
                }
                if (archive.Entries.Count > limits.MaximumEntries)
                {
                    throw new InvalidDataException(
                        $"Excel 工作簿条目数超过 {limits.MaximumEntries} 个上限。");
                }

                long totalExpandedBytes = 0;
                var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    string entryName = NormalizeEntryName(entry.FullName);
                    if (!entryNames.Add(entryName))
                    {
                        throw new InvalidDataException($"Excel 工作簿包含重复条目：{entryName}");
                    }
                    if (entry.Length < 0 || entry.CompressedLength < 0)
                    {
                        throw new InvalidDataException($"Excel 工作簿条目大小无效：{entryName}");
                    }
                    if (entry.Length > limits.MaximumEntryBytes)
                    {
                        throw new InvalidDataException(
                            $"Excel 工作簿条目展开后超过 {limits.MaximumEntryBytes / (1024 * 1024)} MB 上限：{entryName}");
                    }
                    if (totalExpandedBytes > limits.MaximumTotalExpandedBytes - entry.Length)
                    {
                        throw new InvalidDataException(
                            $"Excel 工作簿展开总大小超过 {limits.MaximumTotalExpandedBytes / (1024 * 1024)} MB 上限。");
                    }
                    totalExpandedBytes += entry.Length;

                    if (entry.Length > 1024L * 1024L)
                    {
                        double ratio = entry.CompressedLength == 0
                            ? double.PositiveInfinity
                            : (double)entry.Length / entry.CompressedLength;
                        if (ratio > limits.MaximumCompressionRatio)
                        {
                            throw new InvalidDataException(
                                $"Excel 工作簿条目压缩比异常：{entryName}");
                        }
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
            {
                throw new InvalidDataException("Excel 工作簿压缩结构无效。", ex);
            }
            finally
            {
                input.Position = originalPosition;
            }
        }

        private static bool IsOpenXmlWorkbook(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeEntryName(string entryName)
        {
            string normalized = (entryName ?? string.Empty).Replace('\\', '/').Trim('/');
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
            {
                throw new InvalidDataException("Excel 工作簿包含无效条目路径。");
            }
            return string.Join('/', segments);
        }

        private static void ValidateLimits(ExcelWorkbookResourceLimits limits)
        {
            if (limits.MaximumPackageBytes <= 0
                || limits.MaximumEntries <= 0
                || limits.MaximumEntryBytes <= 0
                || limits.MaximumTotalExpandedBytes <= 0
                || limits.MaximumEntryBytes > limits.MaximumTotalExpandedBytes
                || limits.MaximumCompressionRatio < 1d)
            {
                throw new ArgumentException("Excel 工作簿资源配额无效。", nameof(limits));
            }
        }
    }
}

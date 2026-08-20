using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public static class EmailAttachmentPolicy
    {
        public const int MaximumAttachmentCount = 10;
        public const long MaximumSingleAttachmentBytes = 10L * 1024L * 1024L;
        public const long MaximumTotalAttachmentBytes = 18L * 1024L * 1024L;

        public static IReadOnlyList<string> ValidateAndNormalize(IEnumerable<string> attachmentPaths)
        {
            var paths = (attachmentPaths ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Distinct(PhysicalPathComparison.Comparer)
                .ToList();
            if (paths.Count > MaximumAttachmentCount)
            {
                throw new ServiceValidationException(
                    $"邮件附件不能超过 {MaximumAttachmentCount} 个。");
            }

            long totalBytes = 0;
            foreach (string path in paths)
            {
                if (!File.Exists(path))
                {
                    throw new ResourceNotFoundException($"附件文件不存在：{path}");
                }

                long length = new FileInfo(path).Length;
                if (length > MaximumSingleAttachmentBytes)
                {
                    throw new PayloadLimitExceededException(MaximumSingleAttachmentBytes);
                }
                if (totalBytes > MaximumTotalAttachmentBytes - length)
                {
                    throw new PayloadLimitExceededException(MaximumTotalAttachmentBytes);
                }
                totalBytes += length;
            }

            return paths;
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ServiceValidationException($"附件路径无效：{ex.Message}", ex);
            }
        }
    }
}

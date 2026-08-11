using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowAttachmentResourcePolicy
    {
        public const int MaximumAttachmentCount = 20;
        public const long MaximumSingleAttachmentBytes = 8L * 1024L * 1024L;
        public const long MaximumTotalAttachmentBytes = 20L * 1024L * 1024L;

        public static IReadOnlyList<SingleWindowAttachmentSource> ValidateAndSelect(
            IReadOnlyList<SingleWindowAttachmentSource> attachments)
        {
            var selected = (attachments ?? [])
                .Where(item => item?.Exists == true)
                .ToList();
            if (selected.Count > MaximumAttachmentCount)
            {
                throw new ServiceValidationException(
                    $"单一窗口附件不能超过 {MaximumAttachmentCount} 个。");
            }

            long totalBytes = 0;
            foreach (var attachment in selected)
            {
                long length = new FileInfo(attachment.FilePath).Length;
                if (length <= 0)
                {
                    throw new ServiceValidationException(
                        $"单一窗口附件不能为空：{Path.GetFileName(attachment.FilePath)}");
                }
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

            return selected;
        }
    }
}

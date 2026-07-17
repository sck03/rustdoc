using System.Collections.Generic;

namespace ExportDocManager.Models.Entities
{
    public static class SingleWindowBatchStatusCatalog
    {
        public const string SubmitPackageExported = "SubmitPackageExported";
        public const string SubmitPackageImported = "SubmitPackageImported";
        public const string ReceiptPackageExported = "ReceiptPackageExported";
        public const string ReceiptImported = "ReceiptImported";
        public const string QueuedToClient = "QueuedToClient";
        public const string Received = "Received";
        public const string Accepted = "Accepted";
        public const string PendingReview = "PendingReview";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Failed = "Failed";

        private static readonly IReadOnlyDictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SubmitPackageExported] = "已导出提交包",
                [SubmitPackageImported] = "已导入提交包",
                [ReceiptPackageExported] = "已导出回执包",
                [ReceiptImported] = "已导入回执包",
                [QueuedToClient] = "已送入导入目录",
                [Received] = "已接收",
                [Accepted] = "已受理",
                [PendingReview] = "待审核",
                [Approved] = "已通过",
                [Rejected] = "已退回",
                [Failed] = "失败"
            };

        public static string Normalize(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            var trimmed = status.Trim();
            return DisplayNames.Keys.FirstOrDefault(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
                ?? trimmed;
        }

        public static string GetDisplayName(string status)
        {
            var normalized = Normalize(status);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return DisplayNames.TryGetValue(normalized, out var displayName)
                ? displayName
                : normalized;
        }
    }
}

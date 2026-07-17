namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public sealed class SingleWindowTrackingImportResult
    {
        public int BatchId { get; init; }

        public string Status { get; init; } = string.Empty;

        public int SavedReceiptCount { get; init; }
    }

    public sealed class SingleWindowOperationCenterQuery
    {
        public string BusinessType { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Keyword { get; init; } = string.Empty;

        public int Take { get; init; } = 200;
    }

    public sealed class SingleWindowOperationCenterPageQuery
    {
        public string BusinessType { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Keyword { get; init; } = string.Empty;

        public int PageNumber { get; init; } = 1;

        public int PageSize { get; init; } = 50;
    }

    public sealed class SingleWindowOperationCenterPageResult
    {
        public IReadOnlyList<SingleWindowOperationCenterRow> Rows { get; init; } = [];

        public int TotalCount { get; init; }

        public int PageNumber { get; init; } = 1;

        public int PageSize { get; init; } = 50;

        public int TotalPages => PageSize <= 0
            ? 0
            : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public sealed class SingleWindowOperationCenterRow
    {
        public int BatchId { get; init; }

        public string BatchReference { get; init; } = string.Empty;

        public int SubmissionVersion { get; init; }

        public int DraftRevision { get; init; }

        public string BusinessType { get; init; } = string.Empty;

        public string InvoiceNo { get; init; } = string.Empty;

        public string ContractNo { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string ReferenceNo { get; init; } = string.Empty;

        public string LastReceiptCode { get; init; } = string.Empty;

        public string LastReceiptMessage { get; init; } = string.Empty;

        public string CreatedOnMachine { get; init; } = string.Empty;

        public string SubmitPackagePath { get; init; } = string.Empty;

        public string ClientProfileName { get; init; } = string.Empty;

        public string ClientDispatchPath { get; init; } = string.Empty;

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public int ReceiptCount { get; init; }
    }

    public sealed class SingleWindowOperationCenterPackageRecord
    {
        public string PackageType { get; init; } = string.Empty;

        public string Direction { get; init; } = string.Empty;

        public string FilePath { get; init; } = string.Empty;

        public string CreatedOnMachine { get; init; } = string.Empty;

        public int PayloadFileCount { get; init; }

        public int AttachmentFileCount { get; init; }

        public int WarningCount { get; init; }

        public DateTime CreatedAt { get; init; }
    }

    public sealed class SingleWindowOperationCenterReceiptRecord
    {
        public string ReceiptKind { get; init; } = string.Empty;

        public string ReferenceNo { get; init; } = string.Empty;

        public string ReceiptCode { get; init; } = string.Empty;

        public string ReceiptMessage { get; init; } = string.Empty;

        public string BusinessStatus { get; init; } = string.Empty;

        public string SourceFileName { get; init; } = string.Empty;

        public DateTime ImportedAt { get; init; }

        public DateTime? OccurredAt { get; init; }
    }

    public sealed class SingleWindowOperationCenterDetail
    {
        public int BatchId { get; init; }

        public string BatchReference { get; init; } = string.Empty;

        public int SubmissionVersion { get; init; }

        public int DraftRevision { get; init; }

        public string BusinessType { get; init; } = string.Empty;

        public string InvoiceNo { get; init; } = string.Empty;

        public string ContractNo { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string ReferenceNo { get; init; } = string.Empty;

        public string SubmitPackagePath { get; init; } = string.Empty;

        public string LastReceiptPackagePath { get; init; } = string.Empty;

        public string WorkingDirectoryPath { get; init; } = string.Empty;

        public string ClientProfileName { get; init; } = string.Empty;

        public string ClientDispatchPath { get; init; } = string.Empty;

        public string CreatedOnMachine { get; init; } = string.Empty;

        public int PayloadFileCount { get; init; }

        public int AttachmentFileCount { get; init; }

        public int WarningCount { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime UpdatedAt { get; init; }

        public DateTime? LastReceiptAt { get; init; }

        public DateTime? LastClientDispatchAt { get; init; }

        public IReadOnlyList<SingleWindowOperationCenterPackageRecord> PackageRecords { get; init; } = [];

        public IReadOnlyList<SingleWindowOperationCenterReceiptRecord> ReceiptRecords { get; init; } = [];
    }

    public static class SingleWindowDisplayTextHelper
    {
        public static string GetBusinessTypeDisplayName(SingleWindowBusinessType businessType)
        {
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => "海关原产地证",
                SingleWindowBusinessType.AgentConsignment => "报关代理委托",
                _ => businessType.ToString()
            };
        }

        public static string GetBusinessTypeDisplayName(string businessType)
        {
            return Enum.TryParse<SingleWindowBusinessType>(businessType, true, out var parsed)
                ? GetBusinessTypeDisplayName(parsed)
                : businessType ?? string.Empty;
        }

        public static string GetReceiptKindDisplayName(SingleWindowReceiptKind receiptKind)
        {
            return receiptKind switch
            {
                SingleWindowReceiptKind.CustomsCooBusinessReceipt => "原产地证业务回执",
                SingleWindowReceiptKind.CustomsCooTechnicalReceipt => "原产地证技术回执",
                SingleWindowReceiptKind.CustomsCooAttachmentReceipt => "原产地证附件回执",
                SingleWindowReceiptKind.AgentConsignmentImportResponse => "代理委托导入响应",
                SingleWindowReceiptKind.AgentConsignmentAcd002 => "代理委托协议回执",
                _ => "未知回执"
            };
        }

        public static string GetReceiptKindDisplayName(string receiptKind)
        {
            return Enum.TryParse<SingleWindowReceiptKind>(receiptKind, true, out var parsed)
                ? GetReceiptKindDisplayName(parsed)
                : receiptKind ?? string.Empty;
        }

        public static string GetBusinessStatusDisplayName(SingleWindowReceiptBusinessStatus status)
        {
            return status switch
            {
                SingleWindowReceiptBusinessStatus.Received => "已接收",
                SingleWindowReceiptBusinessStatus.Accepted => "已受理",
                SingleWindowReceiptBusinessStatus.Rejected => "已退回",
                SingleWindowReceiptBusinessStatus.PendingReview => "待审核",
                SingleWindowReceiptBusinessStatus.Approved => "已通过",
                SingleWindowReceiptBusinessStatus.Failed => "失败",
                _ => "未知"
            };
        }

        public static string GetBusinessStatusDisplayName(string status)
        {
            return Enum.TryParse<SingleWindowReceiptBusinessStatus>(status, true, out var parsed)
                ? GetBusinessStatusDisplayName(parsed)
                : status ?? string.Empty;
        }
    }
}

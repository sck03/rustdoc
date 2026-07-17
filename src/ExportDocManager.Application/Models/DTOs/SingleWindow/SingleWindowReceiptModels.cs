namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public enum SingleWindowReceiptKind
    {
        Unknown = 0,
        CustomsCooBusinessReceipt = 1,
        CustomsCooTechnicalReceipt = 2,
        CustomsCooAttachmentReceipt = 3,
        AgentConsignmentImportResponse = 4,
        AgentConsignmentAcd002 = 5
    }

    public enum SingleWindowReceiptBusinessStatus
    {
        Unknown = 0,
        Received = 1,
        Accepted = 2,
        Rejected = 3,
        PendingReview = 4,
        Approved = 5,
        Failed = 6
    }

    public sealed class SingleWindowReceiptParseResult
    {
        public SingleWindowBusinessType BusinessType { get; init; }

        public SingleWindowReceiptKind ReceiptKind { get; init; }

        public string ReferenceNo { get; init; } = string.Empty;

        public string ReceiptCode { get; init; } = string.Empty;

        public string ReceiptMessage { get; init; } = string.Empty;

        public SingleWindowReceiptBusinessStatus BusinessStatus { get; init; }

        public DateTime? OccurredAt { get; init; }

        public string SourceFileName { get; init; } = string.Empty;
    }

    public sealed class SingleWindowReceiptImportEntry
    {
        public SingleWindowReceiptParseResult Receipt { get; init; } = new();

        public string RawContent { get; init; } = string.Empty;
    }
}

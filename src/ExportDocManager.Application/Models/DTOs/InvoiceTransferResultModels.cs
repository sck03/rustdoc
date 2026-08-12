namespace ExportDocManager.Models.DTOs
{
    public class InvoiceTransferPreview
    {
        public string InvoiceNo { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public bool CustomerExists { get; set; }
        public bool ExporterExists { get; set; }
        public bool InvoiceExists { get; set; }
        public bool InvoiceMatches { get; set; }
        public int ExistingInvoiceId { get; set; }
    }

    public enum InvoiceImportConflictAction
    {
        Skip,
        Overwrite,
        NewInvoiceNo,
        AppendItems
    }

    public class InvoiceImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? InvoiceId { get; set; }
        public string FinalInvoiceNo { get; set; } = string.Empty;
        public InvoiceImportConflictAction ActionTaken { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwSubmissionBatch
    {
        public int Id { get; set; }

        [MaxLength(40)]
        public string BatchReference { get; set; } = string.Empty;

        [MaxLength(40)]
        public string BusinessType { get; set; } = string.Empty;

        public int SourceInvoiceId { get; set; }

        [MaxLength(80)]
        public string SourceDocumentType { get; set; } = string.Empty;

        public int SourceDocumentId { get; set; }

        public int SubmissionVersion { get; set; }

        public int DraftRevision { get; set; }

        [MaxLength(80)]
        public string InvoiceNo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ContractNo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Status { get; set; } = SingleWindowBatchStatusCatalog.SubmitPackageExported;

        [MaxLength(40)]
        public string LastBusinessStatus { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ReferenceNo { get; set; } = string.Empty;

        public int PayloadFileCount { get; set; }

        public int AttachmentFileCount { get; set; }

        public int WarningCount { get; set; }

        [MaxLength(80)]
        public string SourceBaselineHash { get; set; } = string.Empty;

        [MaxLength(520)]
        public string SubmitPackagePath { get; set; } = string.Empty;

        [MaxLength(520)]
        public string WorkingDirectoryPath { get; set; } = string.Empty;

        [MaxLength(520)]
        public string LastReceiptPackagePath { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ClientProfileName { get; set; } = string.Empty;

        [MaxLength(520)]
        public string ClientDispatchPath { get; set; } = string.Empty;

        public DateTime? LastClientDispatchAt { get; set; }

        [MaxLength(40)]
        public string LastReceiptKind { get; set; } = string.Empty;

        [MaxLength(80)]
        public string LastReceiptCode { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string LastReceiptMessage { get; set; } = string.Empty;

        [MaxLength(80)]
        public string CreatedOnMachine { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public DateTime? LastReceiptAt { get; set; }

        public List<SwReceiptLog> ReceiptLogs { get; set; } = [];

        public List<SwHandoffPackageRecord> PackageRecords { get; set; } = [];
    }
}

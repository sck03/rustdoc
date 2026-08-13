using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwHandoffPackageRecord
    {
        public int Id { get; set; }

        public int? BatchId { get; set; }

        [MaxLength(40)]
        public string BatchReference { get; set; } = string.Empty;

        [MaxLength(40)]
        public string BusinessType { get; set; } = string.Empty;

        public int SourceInvoiceId { get; set; }

        [MaxLength(80)]
        public string SourceDocumentType { get; set; } = string.Empty;

        public int SourceDocumentId { get; set; }

        [MaxLength(80)]
        public string InvoiceNo { get; set; } = string.Empty;

        [MaxLength(120)]
        public string CompanyScope { get; set; } = string.Empty;

        [MaxLength(64)]
        public string StationKey { get; set; } = string.Empty;

        [MaxLength(40)]
        public string PackageType { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Direction { get; set; } = string.Empty;

        [MaxLength(520)]
        public string FilePath { get; set; } = string.Empty;

        [MaxLength(80)]
        public string CreatedOnMachine { get; set; } = string.Empty;

        public int PayloadFileCount { get; set; }

        public int AttachmentFileCount { get; set; }

        public int WarningCount { get; set; }

        [MaxLength(64)]
        public string ContentDigest { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public string ManifestJson { get; set; } = string.Empty;

        public SwSubmissionBatch? Batch { get; set; }
    }
}

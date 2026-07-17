using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwReceiptLog
    {
        public int Id { get; set; }

        public int BatchId { get; set; }

        [MaxLength(40)]
        public string BusinessType { get; set; } = string.Empty;

        [MaxLength(40)]
        public string ReceiptKind { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ReferenceNo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ReceiptCode { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string ReceiptMessage { get; set; } = string.Empty;

        [MaxLength(40)]
        public string BusinessStatus { get; set; } = string.Empty;

        [MaxLength(260)]
        public string SourceFileName { get; set; } = string.Empty;

        public DateTime ImportedAt { get; set; } = DateTime.Now;

        public DateTime? OccurredAt { get; set; }

        public string RawContent { get; set; } = string.Empty;

        public SwSubmissionBatch Batch { get; set; }
    }
}

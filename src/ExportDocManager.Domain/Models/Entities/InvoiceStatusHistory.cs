using System;

namespace ExportDocManager.Models.Entities
{
    public sealed class InvoiceStatusHistory
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public string FromStatus { get; set; } = string.Empty;
        public string ToStatus { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public int? ChangedByUserId { get; set; }
        public string ChangedByUsername { get; set; } = string.Empty;
        public DateTimeOffset ChangedAt { get; set; }
    }
}

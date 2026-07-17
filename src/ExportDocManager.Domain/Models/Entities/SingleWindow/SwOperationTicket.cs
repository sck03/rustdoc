using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwOperationTicket
    {
        public int Id { get; set; }

        [MaxLength(40)]
        public string BusinessType { get; set; } = string.Empty;

        public int SourceInvoiceId { get; set; }

        public int DocumentId { get; set; }

        public int? BatchId { get; set; }

        [MaxLength(40)]
        public string Status { get; set; } = "Pending";

        [MaxLength(80)]
        public string RequestedBy { get; set; } = string.Empty;

        [MaxLength(80)]
        public string AssignedOperator { get; set; } = string.Empty;

        public int? AssignedWorkstationId { get; set; }

        public int Priority { get; set; } = 0;

        public DateTime RequestedAt { get; set; } = DateTime.Now;

        public DateTime? AssignedAt { get; set; }

        public DateTime? SubmittedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string LastError { get; set; } = string.Empty;
    }
}

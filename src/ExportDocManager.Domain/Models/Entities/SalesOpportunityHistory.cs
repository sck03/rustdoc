using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class SalesOpportunityHistory
    {
        public int Id { get; set; }
        public int SalesOpportunityId { get; set; }
        public SalesOpportunity Opportunity { get; set; }
        public int VersionNumber { get; set; }
        [MaxLength(30)] public string ChangeType { get; set; } = string.Empty;
        [MaxLength(30)] public string Stage { get; set; } = string.Empty;
        [MaxLength(100)] public string QuotationNo { get; set; } = string.Empty;
        public decimal EstimatedAmount { get; set; }
        [MaxLength(3)] public string Currency { get; set; } = string.Empty;
        public int ProbabilityPercent { get; set; }
        public DateTimeOffset? ExpectedCloseAt { get; set; }
        [MaxLength(1000)] public string ChangeNote { get; set; } = string.Empty;
        [MaxLength(100)] public string ChangedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

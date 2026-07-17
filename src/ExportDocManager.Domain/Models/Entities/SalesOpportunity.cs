using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class SalesOpportunity
    {
        public int Id { get; set; }
        public int? OwnerUserId { get; set; }
        [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
        [MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
        public int CrmCustomerId { get; set; }
        public int? ProductId { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
        [MaxLength(30)] public string Stage { get; set; } = "线索";
        [MaxLength(100)] public string QuotationNo { get; set; } = string.Empty;
        public decimal EstimatedAmount { get; set; }
        [MaxLength(3)] public string Currency { get; set; } = "USD";
        public int ProbabilityPercent { get; set; }
        public DateTimeOffset? ExpectedCloseAt { get; set; }
        [MaxLength(500)] public string NextAction { get; set; } = string.Empty;
        [MaxLength(2000)] public string Notes { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class SalesOpportunity : IBusinessOwnedEntity
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
        /// <summary>Canonical nullable key used for global quote tracking uniqueness.</summary>
        [MaxLength(100)] public string? QuotationNoNormalized { get; set; }
        public decimal EstimatedAmount { get; set; }
        [MaxLength(3)] public string Currency { get; set; } = "USD";
        public int ProbabilityPercent { get; set; }
        public DateOnly? ExpectedCloseDate { get; set; }
        [MaxLength(500)] public string NextAction { get; set; } = string.Empty;
        [MaxLength(2000)] public string Notes { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
        /// <summary>Soft-delete marker; history and audit rows remain queryable to administrators.</summary>
        public bool IsDeleted { get; set; }
    }
}

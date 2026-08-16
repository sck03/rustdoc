using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class SupplierAssessment
    {
        public int Id { get; set; }
        public int SupplierCompanyId { get; set; }
        public DateOnly AssessmentDate { get; set; }
        [Required, MaxLength(30)] public string AssessmentKind { get; set; } = "定期评价";
        public int QualityScore { get; set; }
        public int DeliveryScore { get; set; }
        public int ServiceScore { get; set; }
        public int PriceScore { get; set; }
        [Required, MaxLength(30)] public string Conclusion { get; set; } = "合格";
        [MaxLength(1000)] public string Notes { get; set; } = string.Empty;
        [MaxLength(100)] public string AssessedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    }
}

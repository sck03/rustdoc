using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExportDocManager.Models.Entities
{
    [Table("HsCodeDeclarationExamples")]
    public sealed class HsCodeDeclarationExample
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(64)] public string Fingerprint { get; set; } = string.Empty;
        [Required, MaxLength(20)] public string RawReportedHsCode { get; set; } = string.Empty;
        [MaxLength(20)] public string ResolvedCurrentHsCode { get; set; }
        [Required, MaxLength(300)] public string ProductName { get; set; } = string.Empty;
        [MaxLength(1500)] public string Specification { get; set; }
        [Required, MaxLength(2000)] public string SearchText { get; set; } = string.Empty;
        [Required, MaxLength(100)] public string Source { get; set; } = string.Empty;
        public int? SourceYear { get; set; }
        [Required, MaxLength(30)] public string ResolutionStatus { get; set; } = "Unresolved";
        public bool IsManuallyVerified { get; set; }
        public int UseCount { get; set; }
        public int RejectedCount { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("HsCodeReplacementRelations")]
    public sealed class HsCodeReplacementRelation
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(20)] public string OldCode { get; set; } = string.Empty;
        [Required, MaxLength(20)] public string NewCode { get; set; } = string.Empty;
        public int? EffectiveYear { get; set; }
        [Required, MaxLength(100)] public string Source { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public bool IsManuallyVerified { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("HsCodeSearchFeedback")]
    public sealed class HsCodeSearchFeedback
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(64)] public string Fingerprint { get; set; } = string.Empty;
        [Required, MaxLength(500)] public string QueryText { get; set; } = string.Empty;
        [MaxLength(300)] public string ProductName { get; set; }
        [MaxLength(1500)] public string Specification { get; set; }
        [Required, MaxLength(20)] public string CandidateCode { get; set; } = string.Empty;
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
        public DateTime? LastConfirmedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

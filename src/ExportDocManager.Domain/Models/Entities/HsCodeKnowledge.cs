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
        [MaxLength(20)] public string? ResolvedCurrentHsCode { get; set; }
        [Required, MaxLength(300)] public string ProductName { get; set; } = string.Empty;
        [MaxLength(1500)] public string? Specification { get; set; }
        [Required, MaxLength(2000)] public string SearchText { get; set; } = string.Empty;
        [Required, MaxLength(100)] public string Source { get; set; } = string.Empty;
        public int? SourceYear { get; set; }
        [Required, MaxLength(30)] public string ResolutionStatus { get; set; } = "Unresolved";
        public bool IsManuallyVerified { get; set; }
        public int UseCount { get; set; }
        public int RejectedCount { get; set; }
        public DateTimeOffset? LastUsedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
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
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("HsCodeSearchFeedback")]
    public sealed class HsCodeSearchFeedback
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(64)] public string Fingerprint { get; set; } = string.Empty;
        [Required, MaxLength(500)] public string QueryText { get; set; } = string.Empty;
        [MaxLength(300)] public string? ProductName { get; set; }
        [MaxLength(1500)] public string? Specification { get; set; }
        [Required, MaxLength(20)] public string CandidateCode { get; set; } = string.Empty;
        public int AcceptedCount { get; set; }
        public int RejectedCount { get; set; }
        public DateTimeOffset? LastConfirmedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("HsCodeRemoteCandidates")]
    public sealed class HsCodeRemoteCandidate
    {
        [Key] public int Id { get; set; }
        [Required, MaxLength(64)] public string Fingerprint { get; set; } = string.Empty;
        [Required, MaxLength(500)] public string QueryText { get; set; } = string.Empty;
        [Required, MaxLength(20)] public string RawReportedHsCode { get; set; } = string.Empty;
        [MaxLength(20)] public string? SuggestedCurrentHsCode { get; set; }
        [Required, MaxLength(300)] public string ProductName { get; set; } = string.Empty;
        [MaxLength(1500)] public string? Specification { get; set; }
        [Required, MaxLength(100)] public string Source { get; set; } = "i5a6";
        [MaxLength(1000)] public string? SourceUrl { get; set; }
        [Required, MaxLength(30)] public string ReviewStatus { get; set; } = "Pending";
        [Required, MaxLength(30)] public string ResolutionStatus { get; set; } = "Unresolved";
        public int SeenCount { get; set; } = 1;
        public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ReviewedAt { get; set; }
    }
}

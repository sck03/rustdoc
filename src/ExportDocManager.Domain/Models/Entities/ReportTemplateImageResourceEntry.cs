using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public static class ReportTemplateResourceReferenceKind
    {
        public const string Draft = "Draft";
        public const string Published = "Published";
    }

    public sealed class ReportTemplateImageResourceEntry
    {
        [Key, MaxLength(80)]
        public string Id { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string Sha256 { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string MediaType { get; set; } = string.Empty;

        public long ByteLength { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? RecycledAt { get; set; }

        [ConcurrencyCheck]
        public int VersionNumber { get; set; } = 1;
    }

    public sealed class ReportTemplateImageResourceUploadClaim
    {
        [MaxLength(80)]
        public string ResourceId { get; set; } = string.Empty;
        public ReportTemplateImageResourceEntry Resource { get; set; } = null!;
        public int UserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class UserReportTemplateResourceReference
    {
        public int UserReportTemplateId { get; set; }
        public UserReportTemplate Template { get; set; } = null!;

        [MaxLength(80)]
        public string ResourceId { get; set; } = string.Empty;
        public ReportTemplateImageResourceEntry Resource { get; set; } = null!;

        [Required, MaxLength(20)]
        public string ReferenceKind { get; set; } = ReportTemplateResourceReferenceKind.Draft;

        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// Keeps resources used by an immutable template version alive. Current
    /// visibility is still decided through <see cref="UserReportTemplateResourceReference"/>;
    /// history references exist only for integrity and reclamation decisions.
    /// </summary>
    public sealed class UserReportTemplateVersionResourceReference
    {
        public int UserReportTemplateVersionId { get; set; }
        public UserReportTemplateVersion Version { get; set; } = null!;

        [MaxLength(80)]
        public string ResourceId { get; set; } = string.Empty;
        public ReportTemplateImageResourceEntry Resource { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }
    }
}

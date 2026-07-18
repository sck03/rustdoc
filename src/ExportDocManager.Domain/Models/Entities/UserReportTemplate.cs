using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Multi-user report designer template metadata and content.
    /// File-based built-in templates remain available for desktop editions;
    /// this entity is the authoritative store for user-created web/container templates.
    /// </summary>
    public sealed class UserReportTemplate
    {
        public int Id { get; set; }

        [Required, MaxLength(40)]
        public string ReportType { get; set; } = "ExportDocument";

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string ContentHtml { get; set; } = string.Empty;

        public int? OwnerUserId { get; set; }

        [MaxLength(50)]
        public string DepartmentId { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CompanyScope { get; set; } = string.Empty;

        public bool IsShared { get; set; }
        [Required, MaxLength(20)]
        public string ShareScope { get; set; } = "Private";
        public bool IsActive { get; set; } = true;
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

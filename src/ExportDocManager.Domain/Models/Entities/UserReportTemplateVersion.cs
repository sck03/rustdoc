using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class UserReportTemplateVersion
    {
        public int Id { get; set; }
        public int UserReportTemplateId { get; set; }
        public UserReportTemplate Template { get; set; } = null!;
        public int VersionNumber { get; set; }

        [Required, MaxLength(30)]
        public string ChangeType { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string ContentHtml { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsShared { get; set; }

        [Required, MaxLength(20)]
        public string ShareScope { get; set; } = "Private";

        [MaxLength(100)]
        public string ChangedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class EmailTemplate
    {
        public int Id { get; set; }
        public int? OwnerUserId { get; set; }
        [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
        [MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
        [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
        [MaxLength(50)] public string Category { get; set; } = "通用";
        [MaxLength(300)] public string Subject { get; set; } = string.Empty;
        [MaxLength(10000)] public string BodyHtml { get; set; } = string.Empty;
        [Required, MaxLength(20)]
        public string Status { get; set; } = TemplateLifecycleStatusCatalog.Draft;
        [Required, MaxLength(20)]
        public string ShareScope { get; set; } = TemplateShareScopeCatalog.Private;
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}

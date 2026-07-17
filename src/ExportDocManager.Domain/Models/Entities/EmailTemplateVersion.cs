using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class EmailTemplateVersion
    {
        public int Id { get; set; }
        public int EmailTemplateId { get; set; }
        public EmailTemplate Template { get; set; }
        public int VersionNumber { get; set; }
        [MaxLength(30)] public string ChangeType { get; set; } = string.Empty;
        [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
        [MaxLength(50)] public string Category { get; set; } = "通用";
        [MaxLength(300)] public string Subject { get; set; } = string.Empty;
        [MaxLength(10000)] public string BodyHtml { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool IsShared { get; set; }
        [MaxLength(100)] public string ChangedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

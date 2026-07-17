using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class PermissionTemplate
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public bool IsSystem { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public List<PermissionTemplateModule> Modules { get; set; } = [];
    }

    public sealed class PermissionTemplateModule
    {
        public int Id { get; set; }

        public int PermissionTemplateId { get; set; }

        [Required, MaxLength(100)]
        public string ModuleKey { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string AccessLevel { get; set; } = "operate";

        public PermissionTemplate PermissionTemplate { get; set; }
    }
}

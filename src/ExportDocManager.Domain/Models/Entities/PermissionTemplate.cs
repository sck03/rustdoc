using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class PermissionTemplate
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string CodeNormalized { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public bool IsSystem { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTimeOffset UpdatedAt { get; set; }

        [ConcurrencyCheck]
        public int VersionNumber { get; set; } = 1;

        public List<PermissionTemplateGrant> Grants { get; set; } = [];
    }

    public sealed class PermissionTemplateGrant
    {
        public int Id { get; set; }

        public int PermissionTemplateId { get; set; }

        [Required, MaxLength(100)]
        public string ResourceKey { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Action { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string DataScope { get; set; } = "own";

        public PermissionTemplate? PermissionTemplate { get; set; }
    }
}

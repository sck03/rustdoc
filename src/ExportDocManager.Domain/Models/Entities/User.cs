using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExportDocManager.Models.Entities
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; }

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; }

        [MaxLength(100)]
        public string FullName { get; set; }

        [Required]
        [MaxLength(20)]
        // Built-in role identity: Admin, User, Finance, or Sales.
        // Future module permission templates must use a separate relation instead of overloading this field.
        public string Role { get; set; } = "User";

        public int? PermissionTemplateId { get; set; }

        public PermissionTemplate PermissionTemplate { get; set; }

        [NotMapped]
        public IReadOnlyDictionary<string, string> EffectiveModuleAccess { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [MaxLength(50)]
        public string DepartmentId { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CompanyScope { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}

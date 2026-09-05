using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExportDocManager.Models.Entities
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Canonical key used by the database uniqueness constraint.  Keeping
        /// the display value separate means usernames remain readable while
        /// uniqueness is deterministic on every supported provider.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string UsernameNormalized { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? FullName { get; set; }

        [Required]
        [MaxLength(20)]
        // Built-in role identity: Admin, User, Finance, or Sales.
        // Future module permission templates must use a separate relation instead of overloading this field.
        public string Role { get; set; } = "User";

        public int? PermissionTemplateId { get; set; }

        public PermissionTemplate? PermissionTemplate { get; set; }

        [NotMapped]
        public IReadOnlyDictionary<string, string> EffectivePermissionGrants { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [MaxLength(50)]
        public string? DepartmentId { get; set; }

        [MaxLength(50)]
        public string? CompanyScope { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Application-managed optimistic concurrency token.  Account edits
        /// and deletes must carry the value returned by the last read.
        /// </summary>
        [ConcurrencyCheck]
        public int VersionNumber { get; set; } = 1;
    }
}

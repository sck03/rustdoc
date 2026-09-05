using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public static class OrganizationDirectoryDefaults
    {
        public const string CompanyCode = "DEFAULT";
        public const string DepartmentCode = "GENERAL";
    }

    /// <summary>
    /// Stable organization scope identity. Code is the persisted authorization
    /// key copied to business aggregates; display names may change independently.
    /// </summary>
    public sealed class OrganizationCompany
    {
        [Key, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    }

    public sealed class OrganizationDepartment
    {
        [Key, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string CompanyCode { get; set; } = string.Empty;

        public OrganizationCompany? Company { get; set; }

        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    }
}

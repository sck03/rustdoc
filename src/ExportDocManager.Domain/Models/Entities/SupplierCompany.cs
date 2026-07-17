using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class SupplierCompany
    {
        public int Id { get; set; }
        public int? OwnerUserId { get; set; }
        [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
        [MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
        [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
        [MaxLength(100)] public string CountryRegion { get; set; } = string.Empty;
        [MaxLength(100)] public string Category { get; set; } = string.Empty;
        [MaxLength(300)] public string Website { get; set; } = string.Empty;
        [MaxLength(30)] public string Status { get; set; } = "合作中";
        [MaxLength(500)] public string MainProducts { get; set; } = string.Empty;
        [MaxLength(1000)] public string Notes { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class SupplierContact
    {
        public int Id { get; set; }
        public int SupplierCompanyId { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        [MaxLength(100)] public string Title { get; set; } = string.Empty;
        [MaxLength(200)] public string Email { get; set; } = string.Empty;
        [MaxLength(100)] public string Phone { get; set; } = string.Empty;
        [MaxLength(100)] public string InstantMessaging { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

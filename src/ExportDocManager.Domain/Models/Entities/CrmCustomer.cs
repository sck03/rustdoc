using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class CrmCustomer
    {
        public int Id { get; set; }
        public int? LinkedDocumentCustomerId { get; set; }
        public int? OwnerUserId { get; set; }
        [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
        [MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
        [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
        [MaxLength(100)] public string CountryRegion { get; set; } = string.Empty;
        [MaxLength(300)] public string Website { get; set; } = string.Empty;
        [MaxLength(30)] public string Status { get; set; } = "潜在客户";
        [MaxLength(50)] public string Source { get; set; } = string.Empty;
        [MaxLength(1000)] public string Notes { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    }

    public sealed class CrmContact
    {
        public int Id { get; set; }
        public int CrmCustomerId { get; set; }
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        [MaxLength(100)] public string Title { get; set; } = string.Empty;
        [MaxLength(200)] public string Email { get; set; } = string.Empty;
        [MaxLength(100)] public string Phone { get; set; } = string.Empty;
        [MaxLength(100)] public string InstantMessaging { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    }

    public sealed class CrmFollowUp
    {
        public int Id { get; set; }
        public int CrmCustomerId { get; set; }
        public int? CrmContactId { get; set; }
        public int? OwnerUserId { get; set; }
        [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
        [MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
        [Required, MaxLength(30)] public string Type { get; set; } = "其他";
        [Required, MaxLength(500)] public string Summary { get; set; } = string.Empty;
        [MaxLength(300)] public string NextAction { get; set; } = string.Empty;
        public DateTimeOffset FollowedUpAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? NextFollowUpAt { get; set; }
        public bool IsCompleted { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    }
}

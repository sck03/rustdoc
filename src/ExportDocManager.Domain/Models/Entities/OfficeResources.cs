using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities;

public sealed class MeetingRoom
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string NameNormalized { get; set; } = string.Empty;
    [MaxLength(200)] public string Location { get; set; } = string.Empty;
    [MaxLength(500)] public string Equipment { get; set; } = string.Empty;
    public int Capacity { get; set; } = 10;
    public int MaximumBookingHours { get; set; } = 8;
    public int AdvanceBookingDays { get; set; } = 90;
    public bool RequiresKey { get; set; } = true;
    public bool IsActive { get; set; } = true;
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OfficeSupply
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string NameNormalized { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Unit { get; set; } = "件";
    [MaxLength(200)] public string Location { get; set; } = string.Empty;
    [MaxLength(500)] public string Description { get; set; } = string.Empty;
    public bool IsReturnable { get; set; }
    public bool IsActive { get; set; } = true;
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int MinimumStock { get; set; }
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

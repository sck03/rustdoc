using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities;

public enum MeetingBookingStatus { Pending, Approved, InUse, Completed, Rejected, Cancelled }
public enum SupplyRequestStatus { Pending, Approved, Issued, Returned, Rejected, Cancelled }

public sealed class MeetingBooking : IBusinessOwnedEntity
{
    public int Id { get; set; }
    public int MeetingRoomId { get; set; }
    public int? EmployeeId { get; set; }
    public Guid RequestKey { get; set; }
    public int? OwnerUserId { get; set; }
    [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ApplicantName { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public int AttendeeCount { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public MeetingBookingStatus Status { get; set; } = MeetingBookingStatus.Pending;
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OfficeSupplyRequest : IBusinessOwnedEntity
{
    public int Id { get; set; }
    public int OfficeSupplyId { get; set; }
    public int? EmployeeId { get; set; }
    public Guid RequestKey { get; set; }
    public int? OwnerUserId { get; set; }
    [MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ApplicantName { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Purpose { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public DateOnly? ReturnDueDate { get; set; }
    public SupplyRequestStatus Status { get; set; } = SupplyRequestStatus.Pending;
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Business-visible handover history, committed with the state change.</summary>
public sealed class OfficeRequestEvent
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    public int? MeetingBookingId { get; set; }
    public int? OfficeSupplyRequestId { get; set; }
    [Required, MaxLength(30)] public string Action { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ActorUserId { get; set; }
    [Required, MaxLength(100)] public string ActorName { get; set; } = string.Empty;
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Append-only stock ledger. Corrections create another movement.</summary>
public sealed class OfficeStockMovement
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    public int OfficeSupplyId { get; set; }
    public int? OfficeSupplyRequestId { get; set; }
    public Guid OperationId { get; set; }
    [Required, MaxLength(30)] public string Kind { get; set; } = string.Empty;
    public int QuantityDelta { get; set; }
    public int StockAfter { get; set; }
    public int ActorUserId { get; set; }
    [Required, MaxLength(100)] public string ActorName { get; set; } = string.Empty;
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

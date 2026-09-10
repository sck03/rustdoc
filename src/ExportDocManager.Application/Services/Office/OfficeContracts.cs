using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Office;

public enum OfficeOperatingMode { Disabled, Team, LocalRegister }

public sealed record OfficeResourceQuery(string? Keyword = null, bool IncludeInactive = false,
    int PageNumber = 1, int PageSize = 24, bool LowStockOnly = false);

public sealed record OfficeRequestQuery(string? Status = null, bool MineOnly = true,
    int? ResourceId = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    int PageNumber = 1, int PageSize = 20, int? RequestId = null, int? ApplicantUserId = null, int? EmployeeId = null);

public sealed record OfficeDecisionRequest(int ExpectedVersion, string Note = "");
public sealed record OfficeReturnRequest(int ExpectedVersion, int Quantity, string Note = "");
public sealed record OfficeStockRequest(Guid OperationId, int Quantity, int ExpectedVersion, string Note);
public enum OfficeWorkflowAction { Approve, Reject, Cancel, Issue, Return }

public sealed record MeetingRoomSaveRequest(string Name, string Location, string Equipment,
    int Capacity, int MaximumBookingHours, int AdvanceBookingDays, bool RequiresKey,
    bool IsActive, int ExpectedVersion);

public sealed record MeetingRoomRecord(int Id, string Name, string Location, string Equipment,
    int Capacity, int MaximumBookingHours, int AdvanceBookingDays, bool RequiresKey,
    bool IsActive, bool InUse, int VersionNumber);

public sealed record MeetingBookingCreateRequest(Guid RequestKey, int MeetingRoomId, string Title,
    int AttendeeCount, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int? EmployeeId = null);
public sealed record MeetingBookingUpdateRequest(int ExpectedVersion, string Title,
    int AttendeeCount, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record MeetingBookingRecord(int Id, int MeetingRoomId, string RoomName, string Location,
    bool RequiresKey, int OwnerUserId, string ApplicantName, string DepartmentId, string Title,
    int AttendeeCount, DateTimeOffset StartsAt, DateTimeOffset EndsAt, MeetingBookingStatus Status,
    DateTimeOffset? IssuedAt, DateTimeOffset? ReturnedAt, DateTimeOffset CreatedAt, int VersionNumber);

/// <summary>Company availability deliberately excludes meeting titles and identities.</summary>
public sealed record MeetingBusySlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt, MeetingBookingStatus Status);

public sealed record OfficeSupplySaveRequest(string Name, string Unit, string Location, string Description,
    bool IsReturnable, bool IsActive, int MinimumStock, int ExpectedVersion);

public sealed record OfficeSupplyRecord(int Id, string Name, string Unit, string Location, string Description,
    bool IsReturnable, bool IsActive, int StockQuantity, int ReservedQuantity, int MinimumStock, int VersionNumber)
{
    public int AvailableQuantity => StockQuantity - ReservedQuantity;
    public bool LowStock => AvailableQuantity <= MinimumStock;
}

public sealed record SupplyRequestCreateRequest(Guid RequestKey, int OfficeSupplyId, int Quantity,
    string Purpose, DateOnly? ReturnDueDate, int? EmployeeId = null);
public sealed record SupplyRequestUpdateRequest(int ExpectedVersion, int Quantity, string Purpose, DateOnly? ReturnDueDate);

public sealed record OfficeSupplyRequestRecord(int Id, int OfficeSupplyId, string SupplyName, string Unit,
    bool IsReturnable, int OwnerUserId, string ApplicantName, string DepartmentId, string Purpose,
    int Quantity, int ReturnedQuantity, DateOnly? ReturnDueDate, SupplyRequestStatus Status,
    DateTimeOffset CreatedAt, int VersionNumber);

public sealed record OfficeRequestEventRecord(int Id, string Action, int Quantity, string ActorName, string Note, DateTimeOffset CreatedAt);
public sealed record OfficeStockMovementRecord(int Id, string Kind, int QuantityDelta, int StockAfter,
    string ActorName, string Note, DateTimeOffset CreatedAt);

public interface IMeetingRoomService
{
    Task<PagedResult<MeetingRoomRecord>> QueryRoomsAsync(OfficeResourceQuery query, CancellationToken cancellationToken = default);
    Task<MeetingRoomRecord> SaveRoomAsync(int id, MeetingRoomSaveRequest request, CancellationToken cancellationToken = default);
    Task DeleteRoomAsync(int id, DeleteRecordRequest request, CancellationToken cancellationToken = default);
    Task<MeetingBookingRecord> UpdateBookingAsync(int id, MeetingBookingUpdateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MeetingBusySlot>> AvailabilityAsync(int roomId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<PagedResult<MeetingBookingRecord>> QueryBookingsAsync(OfficeRequestQuery query, CancellationToken cancellationToken = default);
    Task<MeetingBookingRecord> CreateBookingAsync(MeetingBookingCreateRequest request, CancellationToken cancellationToken = default);
    Task<MeetingBookingRecord> TransitionAsync(int id, OfficeWorkflowAction action, OfficeDecisionRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<OfficeRequestEventRecord>> HistoryAsync(int id, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default);
}

public interface IOfficeSupplyService
{
    Task<PagedResult<OfficeSupplyRecord>> QuerySuppliesAsync(OfficeResourceQuery query, CancellationToken cancellationToken = default);
    Task<OfficeSupplyRecord> SaveSupplyAsync(int id, OfficeSupplySaveRequest request, CancellationToken cancellationToken = default);
    Task DeleteSupplyAsync(int id, DeleteRecordRequest request, CancellationToken cancellationToken = default);
    Task<OfficeSupplyRequestRecord> UpdateRequestAsync(int id, SupplyRequestUpdateRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<OfficeSupplyRequestRecord>> QueryRequestsAsync(OfficeRequestQuery query, CancellationToken cancellationToken = default);
    Task<OfficeSupplyRequestRecord> CreateRequestAsync(SupplyRequestCreateRequest request, CancellationToken cancellationToken = default);
    Task<OfficeSupplyRequestRecord> TransitionAsync(int id, OfficeWorkflowAction action, OfficeReturnRequest request, CancellationToken cancellationToken = default);
    Task<OfficeStockMovementRecord> ChangeStockAsync(int id, OfficeStockRequest request, bool stocktake, CancellationToken cancellationToken = default);
    Task<PagedResult<OfficeStockMovementRecord>> StockHistoryAsync(int id, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResult<OfficeRequestEventRecord>> HistoryAsync(int id, int pageNumber = 1, int pageSize = 50, CancellationToken cancellationToken = default);
}

using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Office;

public sealed record PersonnelQuery(string? Keyword = null, string? DepartmentId = null, string? Status = null,
    bool AttentionOnly = false, int PageNumber = 1, int PageSize = 24);

public sealed record PersonnelProfile(string FullName, string WorkEmail = "", string WorkPhone = "",
    string WorkLocation = "", string PersonalPhone = "", string EmergencyContact = "", string EmergencyPhone = "", string Notes = "");

public sealed record PersonnelCreateRequest(Guid RequestKey, string EmployeeNumber, string DepartmentId, string JobTitle,
    EmploymentType EmploymentType, DateOnly HireDate, bool OnProbation, DateOnly? ProbationEndsOn,
    DateOnly? ContractEndsOn, PersonnelProfile Profile);

public sealed record PersonnelUpdateRequest(int ExpectedVersion, PersonnelProfile Profile, EmploymentType EmploymentType,
    DateOnly? ProbationEndsOn, DateOnly? ContractEndsOn);

public enum PersonnelAction { Confirm, Transfer, Depart, Rehire }
public sealed record PersonnelTransitionRequest(int ExpectedVersion, DateOnly EffectiveDate, string Note,
    string? DepartmentId = null, string? JobTitle = null, bool OnProbation = true);
public sealed record PersonnelAccountRequest(int ExpectedVersion, int UserId, int ExpectedAccountVersion);

/// <summary>The company directory contains work information only.</summary>
public sealed record PersonnelDirectoryRecord(int Id, string EmployeeNumber, string FullName, string DepartmentId,
    string DepartmentName, string JobTitle, string WorkEmail, string WorkPhone, string WorkLocation,
    EmploymentStatus Status, bool CanViewDetails);

public sealed record PersonnelAccountRecord(int Id, string Username, string FullName, string DepartmentId, bool IsActive, int VersionNumber);
public sealed record PersonnelDepartmentRecord(string Code, string Name, bool IsActive);
public sealed record PersonnelOptions(IReadOnlyList<PersonnelDepartmentRecord> Departments, bool CanCreate);

public sealed record PersonnelRecord(PersonnelDirectoryRecord Employee, PersonnelProfile Profile, EmploymentType EmploymentType,
    DateOnly HireDate, DateOnly LastEffectiveDate, DateOnly? ProbationEndsOn, DateOnly? ContractEndsOn,
    DateOnly? ConfirmedOn, DateOnly? DepartedOn, PersonnelAccountRecord? Account, int VersionNumber,
    bool CanEdit, bool CanTransition, bool CanLinkAccount);

public sealed record PersonnelEventRecord(int Id, string Action, DateOnly EffectiveDate, string ActorName,
    string Summary, string Note, DateTimeOffset CreatedAt);

public sealed record PersonnelChangeResult(PersonnelRecord Record, int? ChangedAccountUserId);

public sealed record PersonnelClearanceItem(string Kind, int RequestId, string ResourceName, string Status, int OutstandingQuantity);
public sealed record PersonnelClearance(int MeetingCount, int SupplyCount, IReadOnlyList<PersonnelClearanceItem> Items)
{
    public bool IsClear => MeetingCount == 0 && SupplyCount == 0;
}

public interface IPersonnelService
{
    Task<PagedResult<PersonnelDirectoryRecord>> QueryAsync(PersonnelQuery query, CancellationToken cancellationToken = default);
    Task<PersonnelOptions> OptionsAsync(CancellationToken cancellationToken = default);
    Task<PersonnelRecord> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<PersonnelRecord> CreateAsync(PersonnelCreateRequest request, CancellationToken cancellationToken = default);
    Task<PersonnelChangeResult> UpdateAsync(int id, PersonnelUpdateRequest request, CancellationToken cancellationToken = default);
    Task<PersonnelChangeResult> TransitionAsync(int id, PersonnelAction action, PersonnelTransitionRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<PersonnelAccountRecord>> AccountOptionsAsync(int id, string? keyword, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PersonnelChangeResult> LinkAccountAsync(int id, PersonnelAccountRequest request, CancellationToken cancellationToken = default);
    Task<PersonnelClearance> ClearanceAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<PersonnelEventRecord>> HistoryAsync(int id, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}

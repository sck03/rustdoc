using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities;

public enum EmploymentStatus { Probation, Active, Departed }
public enum EmploymentType { FullTime, PartTime, Intern, Contractor }

/// <summary>Employee identity survives account suspension and employment changes.</summary>
public sealed class PersonnelEmployee : IBusinessOwnedEntity
{
    public int Id { get; set; }
    public Guid RequestKey { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string DepartmentId { get; set; } = string.Empty;
    public OrganizationDepartment? Department { get; set; }
    public int? OwnerUserId { get; set; }
    public User? Account { get; set; }
    [Required, MaxLength(40)] public string EmployeeNumber { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string EmployeeNumberNormalized { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string JobTitle { get; set; } = string.Empty;
    public EmploymentType EmploymentType { get; set; }
    public EmploymentStatus Status { get; set; }
    public DateOnly HireDate { get; set; }
    public DateOnly LastEffectiveDate { get; set; }
    public DateOnly? ProbationEndsOn { get; set; }
    public DateOnly? ContractEndsOn { get; set; }
    public DateOnly? ConfirmedOn { get; set; }
    public DateOnly? DepartedOn { get; set; }
    [MaxLength(254)] public string WorkEmail { get; set; } = string.Empty;
    [MaxLength(50)] public string WorkPhone { get; set; } = string.Empty;
    [MaxLength(120)] public string WorkLocation { get; set; } = string.Empty;
    [MaxLength(50)] public string PersonalPhone { get; set; } = string.Empty;
    [MaxLength(100)] public string EmergencyContact { get; set; } = string.Empty;
    [MaxLength(50)] public string EmergencyPhone { get; set; } = string.Empty;
    [MaxLength(1000)] public string Notes { get; set; } = string.Empty;
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PersonnelEvent
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Action { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public int ActorUserId { get; set; }
    [Required, MaxLength(100)] public string ActorName { get; set; } = string.Empty;
    [MaxLength(2000)] public string Summary { get; set; } = string.Empty;
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

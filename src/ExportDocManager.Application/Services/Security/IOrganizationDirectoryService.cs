using ExportDocManager.Models;

namespace ExportDocManager.Services.Security
{
    public sealed record OrganizationCompanyRecord(
        string Code,
        string Name,
        bool IsActive,
        int VersionNumber);

    public sealed record OrganizationDepartmentRecord(
        string Code,
        string CompanyCode,
        string Name,
        bool IsActive,
        int VersionNumber,
        string? ParentCode = null,
        int? ManagerEmployeeId = null,
        string ManagerName = "");

    public sealed record OrganizationManagerRecord(int Id, string FullName, string EmployeeNumber, string DepartmentName);

    public sealed record OrganizationDirectoryRecord(
        IReadOnlyList<OrganizationCompanyRecord> Companies,
        IReadOnlyList<OrganizationDepartmentRecord> Departments);

    public sealed record OrganizationCompanySaveRequest(
        string ExistingCode,
        string Code,
        string Name,
        bool IsActive,
        int ExpectedVersion = 0);

    public sealed record OrganizationDepartmentSaveRequest(
        string ExistingCode,
        string Code,
        string CompanyCode,
        string Name,
        bool IsActive,
        int ExpectedVersion = 0,
        string? ParentCode = null,
        int? ManagerEmployeeId = null);

    public interface IOrganizationDirectoryService
    {
        Task<OrganizationDirectoryRecord> ListAsync(CancellationToken cancellationToken = default);
        Task<PagedResult<OrganizationManagerRecord>> ManagerOptionsAsync(string companyCode, string? keyword,
            int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        Task<OrganizationCompanyRecord> SaveCompanyAsync(
            OrganizationCompanySaveRequest request,
            CancellationToken cancellationToken = default);

        Task<OrganizationDepartmentRecord> SaveDepartmentAsync(
            OrganizationDepartmentSaveRequest request,
            CancellationToken cancellationToken = default);
    }
}

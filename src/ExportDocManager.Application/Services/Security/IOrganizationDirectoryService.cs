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
        int VersionNumber);

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
        int ExpectedVersion = 0);

    public interface IOrganizationDirectoryService
    {
        Task<OrganizationDirectoryRecord> ListAsync(CancellationToken cancellationToken = default);

        Task<OrganizationCompanyRecord> SaveCompanyAsync(
            OrganizationCompanySaveRequest request,
            CancellationToken cancellationToken = default);

        Task<OrganizationDepartmentRecord> SaveDepartmentAsync(
            OrganizationDepartmentSaveRequest request,
            CancellationToken cancellationToken = default);
    }
}

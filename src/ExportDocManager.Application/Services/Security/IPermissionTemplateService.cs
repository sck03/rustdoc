namespace ExportDocManager.Services.Security
{
    public sealed record PermissionTemplateModuleRecord(string ModuleKey, string AccessLevel);

    public sealed record PermissionTemplateRecord(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsSystem,
        bool IsActive,
        DateTime UpdatedAt,
        IReadOnlyList<PermissionTemplateModuleRecord> Modules);

    public sealed record PermissionTemplateSaveRequest(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsActive,
        IReadOnlyList<PermissionTemplateModuleRecord> Modules);

    public interface IPermissionTemplateService
    {
        Task<IReadOnlyList<PermissionTemplateRecord>> ListAsync(CancellationToken cancellationToken = default);

        Task<PermissionTemplateRecord> SaveAsync(
            PermissionTemplateSaveRequest request,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}

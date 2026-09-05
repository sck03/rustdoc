namespace ExportDocManager.Services.Security
{
    public sealed record PermissionGrantRecord(string ResourceKey, string Action, string DataScope);

    public sealed record PermissionTemplateRecord(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsSystem,
        bool IsActive,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<PermissionGrantRecord> Grants,
        IReadOnlyList<EffectivePermissionGrant> EffectiveGrants,
        int VersionNumber = 1);

    public sealed record PermissionTemplateSaveRequest(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsActive,
        IReadOnlyList<PermissionGrantRecord> Grants,
        int ExpectedVersion = 0);

    public interface IPermissionTemplateService
    {
        Task<IReadOnlyList<PermissionTemplateRecord>> ListAsync(CancellationToken cancellationToken = default);

        Task<PermissionTemplateRecord> SaveAsync(
            PermissionTemplateSaveRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<int>> ListAssignedUserIdsAsync(
            int templateId,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(
            int id,
            CancellationToken cancellationToken = default,
            int expectedVersion = 0);
    }
}

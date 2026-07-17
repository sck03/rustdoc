namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiUserAccountDto(
        int Id,
        string Username,
        string FullName,
        string Role,
        int? PermissionTemplateId,
        string PermissionTemplateCode,
        string PermissionTemplateName,
        string DepartmentId,
        string CompanyScope,
        bool IsActive);

    public sealed record ApiUserListResponse(
        IReadOnlyList<ApiUserAccountDto> Users,
        IReadOnlyList<string> Roles,
        IReadOnlyList<ApiPermissionTemplateOptionDto> PermissionTemplates);

    public sealed record ApiUserSaveRequest(
        string Username,
        string FullName,
        string Role,
        int? PermissionTemplateId,
        string DepartmentId,
        string CompanyScope,
        bool IsActive,
        string ResetPassword);

    public sealed record ApiUserSaveResponse(
        bool Success,
        string Message,
        ApiUserAccountDto User);

    public sealed record ApiPermissionTemplateOptionDto(
        int Id,
        string Code,
        string Name,
        bool IsSystem,
        bool IsActive);

    public sealed record ApiPermissionModuleDefinitionDto(
        string Key,
        string Name,
        string Group,
        string Workspace,
        int SortOrder,
        bool IsTechnical);

    public sealed record ApiPermissionTemplateModuleDto(
        string ModuleKey,
        string AccessLevel);

    public sealed record ApiPermissionTemplateDto(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsSystem,
        bool IsActive,
        DateTime UpdatedAt,
        IReadOnlyList<ApiPermissionTemplateModuleDto> Modules);

    public sealed record ApiPermissionTemplateCatalogResponse(
        IReadOnlyList<ApiPermissionModuleDefinitionDto> Modules,
        IReadOnlyList<ApiPermissionTemplateDto> Templates,
        IReadOnlyList<string> AccessLevels,
        string ApplyPolicy);

    public sealed record ApiPermissionTemplateSaveRequest(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsActive,
        IReadOnlyList<ApiPermissionTemplateModuleDto> Modules);
}

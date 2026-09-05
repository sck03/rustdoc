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
        bool IsActive,
        int VersionNumber = 1);

    public sealed record ApiUserListResponse(
        IReadOnlyList<ApiUserAccountDto> Users,
        IReadOnlyList<string> Roles,
        IReadOnlyList<ApiPermissionTemplateOptionDto> PermissionTemplates,
        IReadOnlyList<ApiOrganizationCompanyDto> Companies,
        IReadOnlyList<ApiOrganizationDepartmentDto> Departments);

    public sealed record ApiOrganizationCompanyDto(
        string Code,
        string Name,
        bool IsActive,
        int VersionNumber);

    public sealed record ApiOrganizationDepartmentDto(
        string Code,
        string CompanyCode,
        string Name,
        bool IsActive,
        int VersionNumber);

    public sealed record ApiOrganizationDirectoryResponse(
        IReadOnlyList<ApiOrganizationCompanyDto> Companies,
        IReadOnlyList<ApiOrganizationDepartmentDto> Departments);

    public sealed record ApiOrganizationCompanySaveRequest(
        string Code,
        string Name,
        bool IsActive,
        int ExpectedVersion = 0);

    public sealed record ApiOrganizationDepartmentSaveRequest(
        string Code,
        string CompanyCode,
        string Name,
        bool IsActive,
        int ExpectedVersion = 0);

    public sealed record ApiUserSaveRequest(
        string Username,
        string FullName,
        string Role,
        int? PermissionTemplateId,
        string DepartmentId,
        string CompanyScope,
        bool IsActive,
        string ResetPassword,
        int ExpectedVersion = 0);

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

    public sealed record ApiPermissionActionDefinitionDto(
        string Key,
        string Name,
        string Description,
        int SortOrder,
        string PresetLevel);

    public sealed record ApiPermissionResourceDefinitionDto(
        string Key,
        string Name,
        string Group,
        string Workspace,
        string ModuleKey,
        int SortOrder,
        bool IsTechnical,
        bool SupportsDataScope,
        IReadOnlyList<ApiPermissionActionDefinitionDto> Actions);

    public sealed record ApiEffectivePermissionGrantDto(
        string ResourceKey,
        string Action,
        string DataScope,
        string Source,
        string SourceResourceKey);

    public sealed record ApiPermissionTemplateDto(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsSystem,
        bool IsActive,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<ApiPermissionGrantDto> Grants,
        IReadOnlyList<ApiEffectivePermissionGrantDto> EffectiveGrants,
        int VersionNumber = 1);

    public sealed record ApiPermissionTemplateCatalogResponse(
        IReadOnlyList<ApiPermissionResourceDefinitionDto> Resources,
        IReadOnlyList<ApiPermissionTemplateDto> Templates,
        IReadOnlyList<string> DataScopes,
        IReadOnlyList<string> AccessLevels,
        string ApplyPolicy);

    public sealed record ApiPermissionTemplateSaveRequest(
        int Id,
        string Code,
        string Name,
        string Description,
        bool IsActive,
        IReadOnlyList<ApiPermissionGrantDto> Grants,
        int ExpectedVersion = 0);
}

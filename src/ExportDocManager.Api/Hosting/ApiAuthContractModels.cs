namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiLoginRequest(string Username, string Password);

    public sealed record ApiLoginResponse(
        string TokenType,
        string AccessToken,
        DateTimeOffset ExpiresAt,
        ApiUserDto User);

    public sealed record ApiUserDto(
        int Id,
        string Username,
        string FullName,
        string Role,
        string DepartmentId,
        string CompanyScope,
        bool IsActive,
        DateOnly BusinessDate,
        string BusinessTimeZone,
        DateTimeOffset BusinessDateValidUntilUtc,
        ApiUserCapabilitiesDto Capabilities);

    public sealed record ApiUserCapabilitiesDto(
        bool CanManageSettings,
        bool CanManageUsers,
        bool CanViewAllBusinessData,
        bool CanUseDocumentWorkspace,
        bool CanUseSalesWorkspace,
        string ProductEdition,
        IReadOnlyList<string> EnabledModules,
        IReadOnlyList<ApiModuleAccessDto> ModuleAccess);

    public sealed record ApiModuleAccessDto(
        string ModuleKey,
        string AccessLevel);

    public sealed record ApiErrorResponse(
        string Message,
        string? Code = null,
        string? CorrelationId = null);

    public sealed record ApiLogoutResponse(bool Success);
}

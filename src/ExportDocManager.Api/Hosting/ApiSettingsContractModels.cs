using ExportDocManager.Models;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiSettingsSecretsDto(
        bool EmailPasswordSet,
        bool WebDavPasswordSet,
        bool PostgreSqlPasswordSet,
        bool AiApiKeySet);

    public sealed record ApiSettingsResponse(
        AppSettings Settings,
        ApiSettingsSecretsDto Secrets,
        string StoragePolicy);

    public sealed record ApiSettingsSaveRequest(
        AppSettings Settings,
        bool UpdateSecrets);

    public sealed record ApiSettingsValidationRequest(
        AppSettings Settings,
        bool UpdateSecrets);

    public sealed record ApiSettingsValidationMessageDto(
        string Level,
        string PropertyName,
        string Message,
        bool IsAutoFixable);

    public sealed record ApiSettingsValidationResponse(
        bool IsValid,
        bool HasWarnings,
        bool CanAutoFix,
        IReadOnlyList<ApiSettingsValidationMessageDto> Messages,
        AppSettings NormalizedSettings,
        string StoragePolicy);

    public sealed record ApiSettingsSaveResponse(
        bool Success,
        bool RequiresRestart,
        AppSettings Settings,
        ApiSettingsSecretsDto Secrets,
        string Message);
}

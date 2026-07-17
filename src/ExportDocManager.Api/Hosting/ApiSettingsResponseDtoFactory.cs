using ExportDocManager.Models;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        public static ApiSettingsResponse FromSettings(AppSettings settings)
        {
            EnsureDefaults(settings);

            return new ApiSettingsResponse(
                CreateSanitizedSettings(settings),
                GetSecrets(settings),
                StoragePolicy);
        }

        public static ApiSettingsSaveResponse FromSavedSettings(
            AppSettings settings,
            bool requiresRestart,
            string message)
        {
            EnsureDefaults(settings);

            return new ApiSettingsSaveResponse(
                true,
                requiresRestart,
                CreateSanitizedSettings(settings),
                GetSecrets(settings),
                message ?? string.Empty);
        }

        private static AppSettings CreateSanitizedSettings(AppSettings settings)
        {
            var clone = CloneSettings(settings);
            EnsureDefaults(clone);
            clone.Email.Password = string.Empty;
            clone.WebDav.Password = string.Empty;
            clone.System.PostgreSqlPassword = string.Empty;
            clone.AI.ApiKey = string.Empty;
            return clone;
        }

        private static ApiSettingsSecretsDto GetSecrets(AppSettings settings)
        {
            EnsureDefaults(settings);

            return new ApiSettingsSecretsDto(
                !string.IsNullOrEmpty(settings.Email.Password),
                !string.IsNullOrEmpty(settings.WebDav.Password),
                !string.IsNullOrEmpty(settings.System.PostgreSqlPassword),
                !string.IsNullOrEmpty(settings.AI.ApiKey));
        }
    }
}

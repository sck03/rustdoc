using ExportDocManager.DataAccess;
using ExportDocManager.Models;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        public static ApiSettingsResponse FromSettings(AppSettings settings)
        {
            return FromSettingsForUser(settings, canManageSettings: true, networkMode: false);
        }

        public static ApiSettingsResponse FromSettingsForUser(
            AppSettings settings,
            bool canManageSettings,
            bool networkMode)
        {
            EnsureDefaults(settings);

            return new ApiSettingsResponse(
                CreateSanitizedSettings(settings, canManageSettings, networkMode),
                GetSecrets(settings, canManageSettings),
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
                CreateSanitizedSettings(settings, canManageSettings: true, networkMode: false),
                GetSecrets(settings, canManageSettings: true),
                message ?? string.Empty);
        }

        private static AppSettings CreateSanitizedSettings(
            AppSettings settings,
            bool canManageSettings,
            bool networkMode)
        {
            var clone = CloneSettings(settings);
            EnsureDefaults(clone);
            clone.Email.Password = string.Empty;
            clone.WebDav.Password = string.Empty;
            clone.System.PostgreSqlPassword = string.Empty;
            clone.AI.ApiKey = string.Empty;

            if (!canManageSettings)
            {
                clone.System.SqliteDatabaseFileName = DatabaseConnectionSettings.DefaultSqliteDatabaseFileName;
                clone.System.PostgreSqlHost = string.Empty;
                clone.System.PostgreSqlPort = 5432;
                clone.System.PostgreSqlDatabase = string.Empty;
                clone.System.PostgreSqlUsername = string.Empty;
                clone.System.PostgreSqlAdditionalOptions = string.Empty;
                if (networkMode)
                {
                    clone.System.DefaultExportDirectory = string.Empty;
                }

                clone.Email.SmtpHost = string.Empty;
                clone.Email.UserName = string.Empty;
                clone.Email.FromAddress = string.Empty;
                clone.Email.FromDisplayName = string.Empty;
                clone.WebDav.Url = string.Empty;
                clone.WebDav.UserName = string.Empty;
                clone.WebDav.Enabled = false;
                clone.AI.ApiEndpoint = string.Empty;
                clone.AI.ModelName = string.Empty;
                clone.AI.SystemPrompt = string.Empty;
                clone.SingleWindow.CustomsCooDefaults = new CustomsCooDefaultProfile();
            }

            return clone;
        }

        private static ApiSettingsSecretsDto GetSecrets(AppSettings settings, bool canManageSettings)
        {
            EnsureDefaults(settings);

            if (!canManageSettings)
            {
                return new ApiSettingsSecretsDto(false, false, false, false);
            }

            return new ApiSettingsSecretsDto(
                !string.IsNullOrEmpty(settings.Email.Password),
                !string.IsNullOrEmpty(settings.WebDav.Password),
                !string.IsNullOrEmpty(settings.System.PostgreSqlPassword),
                !string.IsNullOrEmpty(settings.AI.ApiKey));
        }
    }
}

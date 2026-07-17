using ExportDocManager.Models;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        public static AppSettings PrepareForSave(
            AppSettings requestSettings,
            AppSettings currentSettings,
            bool updateSecrets)
        {
            var prepared = CloneSettings(requestSettings ?? new AppSettings());
            EnsureDefaults(prepared);
            EnsureDefaults(currentSettings);

            if (!updateSecrets)
            {
                prepared.Email.Password = currentSettings.Email.Password;
                prepared.WebDav.Password = currentSettings.WebDav.Password;
                prepared.System.PostgreSqlPassword = currentSettings.System.PostgreSqlPassword;
                prepared.AI.ApiKey = currentSettings.AI.ApiKey;
            }

            return prepared;
        }

        public static void CopyInto(AppSettings target, AppSettings source)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);
            EnsureDefaults(source);

            target.System = source.System;
            target.BatchExport = source.BatchExport;
            target.PaymentTemplates = source.PaymentTemplates;
            target.ExcelImport = source.ExcelImport;
            target.ExcelImportSchemes = source.ExcelImportSchemes;
            target.ExchangeRate = source.ExchangeRate;
            target.Email = source.Email;
            target.WebDav = source.WebDav;
            target.AI = source.AI;
            target.SingleWindow = source.SingleWindow;
        }
    }
}

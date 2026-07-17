using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        public const string StoragePolicy =
            "设置文件默认读取和保存到程序根目录 appsettings.json；不会迁入 App_Data，授权状态保存到运行数据根 Security。";

        private static AppSettings CloneSettings(AppSettings settings)
        {
            if (settings == null)
            {
                return new AppSettings();
            }

            var json = JsonSerializer.Serialize(settings);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        private static void EnsureDefaults(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.System ??= new SystemSettings();
            settings.BatchExport ??= new BatchExportSettings();
            settings.PaymentTemplates ??= new List<BatchExportItem>();
            settings.ExcelImport ??= new ExcelImportSettings();
            settings.ExcelImportSchemes ??= new List<ExcelImportSettings>();
            settings.ExchangeRate ??= new ExchangeRateSettings();
            settings.ExchangeRate.SelectedCurrencies ??= new List<string>();
            settings.ExchangeRate.AllSupportedCurrencies ??= new List<string>();
            settings.Email ??= new EmailConfig();
            settings.Email.DocumentEmailSubjectTemplate = NormalizeTemplateText(
                settings.Email.DocumentEmailSubjectTemplate,
                EmailConfig.DefaultDocumentEmailSubjectTemplate);
            settings.Email.DocumentEmailBodyTemplate = NormalizeTemplateText(
                settings.Email.DocumentEmailBodyTemplate,
                EmailConfig.DefaultDocumentEmailBodyTemplate);
            settings.WebDav ??= new WebDavSettings();
            settings.AI ??= new AISettings();
            settings.SingleWindow ??= new SingleWindowSettings();
            settings.SingleWindow.CustomsCooDefaults ??= new CustomsCooDefaultProfile();

            settings.System.BackupRetentionDays = Math.Max(0, settings.System.BackupRetentionDays);
            settings.System.PostgreSqlAutoBackupSchedule = NormalizePostgreSqlAutoBackupSchedule(
                settings.System.PostgreSqlAutoBackupSchedule);
            settings.System.PostgreSqlAutoBackupTime = NormalizePostgreSqlAutoBackupTime(
                settings.System.PostgreSqlAutoBackupTime);
            settings.System.PostgreSqlAutoBackupDayOfWeek =
                Math.Clamp(settings.System.PostgreSqlAutoBackupDayOfWeek, 0, 6);
            settings.System.PostgreSqlAutoBackupRetentionCount =
                Math.Max(1, settings.System.PostgreSqlAutoBackupRetentionCount);
            settings.System.ItemEntryBlankRowCount = Math.Clamp(settings.System.ItemEntryBlankRowCount, 1, 500);
            settings.System.AuditLogRetentionDays = Math.Max(0, settings.System.AuditLogRetentionDays);
            settings.System.LogRetentionDays = Math.Max(0, settings.System.LogRetentionDays);
            settings.System.LogRetainedFileCount = Math.Max(1, settings.System.LogRetainedFileCount);
            settings.System.LogFileSizeLimitMB = Math.Max(1, settings.System.LogFileSizeLimitMB);
            settings.System.DatabaseProvider = DatabaseModeHelper.NormalizeProvider(settings.System.DatabaseProvider);
            settings.System.SqliteDatabaseFileName =
                DbHelper.NormalizeSqliteDatabaseFileName(settings.System.SqliteDatabaseFileName);
            settings.System.PostgreSqlPort = DbHelper.NormalizePostgreSqlPort(settings.System.PostgreSqlPort);
            settings.System.PostgreSqlHost = DbHelper.NormalizePostgreSqlText(settings.System.PostgreSqlHost);
            settings.System.PostgreSqlDatabase = DbHelper.NormalizePostgreSqlText(settings.System.PostgreSqlDatabase);
            settings.System.PostgreSqlUsername = DbHelper.NormalizePostgreSqlText(settings.System.PostgreSqlUsername);
            settings.System.PostgreSqlAdditionalOptions =
                DbHelper.NormalizePostgreSqlAdditionalOptions(settings.System.PostgreSqlAdditionalOptions);
        }

        private static string NormalizeTemplateText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string NormalizePostgreSqlAutoBackupSchedule(string value)
        {
            return string.Equals(value?.Trim(), "Weekly", StringComparison.OrdinalIgnoreCase)
                ? "Weekly"
                : "Daily";
        }

        private static string NormalizePostgreSqlAutoBackupTime(string value)
        {
            return TimeSpan.TryParse(value?.Trim(), out var time)
                ? new TimeSpan(time.Hours, time.Minutes, 0).ToString(@"hh\:mm")
                : "02:00";
        }
    }
}

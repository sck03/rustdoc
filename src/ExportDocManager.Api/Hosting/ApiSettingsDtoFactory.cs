using System.Globalization;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        public const string StoragePolicy =
            "设置文件读取和保存到运行数据根 Config/appsettings.json；数据库、日志、模板副本和授权镜像均归入运行数据根，程序安装目录保持只读。";

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
            settings.PaymentTemplates ??= new List<PaymentTemplateItem>();
            foreach (var paymentTemplate in settings.PaymentTemplates.Where(item => item != null))
            {
                paymentTemplate.ReportType = ReportDocumentType.PaymentVoucher.ToString();
            }
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
                DbHelper.NormalizeRuntimeSqliteDatabaseFileName(settings.System.SqliteDatabaseFileName);
            settings.System.PostgreSqlPort = DbHelper.NormalizePostgreSqlPort(settings.System.PostgreSqlPort);
            settings.System.PostgreSqlHost = DbHelper.NormalizePostgreSqlText(settings.System.PostgreSqlHost);
            settings.System.PostgreSqlDatabase = DbHelper.NormalizePostgreSqlText(settings.System.PostgreSqlDatabase);
            settings.System.PostgreSqlUsername = DbHelper.NormalizePostgreSqlText(settings.System.PostgreSqlUsername);
            settings.System.PostgreSqlAdditionalOptions =
                DbHelper.NormalizePostgreSqlAdditionalOptions(settings.System.PostgreSqlAdditionalOptions);
            settings.System.UpdaterEndpoint = UpdaterEndpointPolicy.Normalize(settings.System.UpdaterEndpoint);
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
            return TimeSpan.TryParse(value?.Trim(), CultureInfo.InvariantCulture, out var time)
                ? new TimeSpan(time.Hours, time.Minutes, 0).ToString(@"hh\:mm")
                : "02:00";
        }
    }
}

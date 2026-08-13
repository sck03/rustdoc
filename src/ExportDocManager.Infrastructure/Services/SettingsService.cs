using System.Text.Json;
using System.Threading;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public class SettingsService : ISettingsService
    {
        private const string SettingsFileName = "appsettings.json";

        private readonly string _filePath;
        private readonly LocalSecretProtector _secretProtector;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        public AppSettings Settings { get; private set; } = new AppSettings();

        public SettingsService(IAppPathProvider pathProvider)
            : this(pathProvider, null)
        {
        }

        public SettingsService(IAppPathProvider pathProvider, string? filePath)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            _filePath = ResolveSettingsPath(pathProvider, filePath);
            _secretProtector = new LocalSecretProtector(pathProvider);
        }

        public async Task LoadAsync()
        {
            if (!File.Exists(_filePath))
            {
                EnsureSettingsDefaults();
                return;
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"无法读取设置文件 {_filePath}。", ex);
            }

            AppSettings loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<AppSettings>(json)
                    ?? throw new InvalidDataException("设置文件内容为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"设置文件 JSON 损坏：{_filePath}。", ex);
            }

            var previous = Settings;
            Settings = loaded;
            try
            {
                EnsureSettingsDefaults();
                Settings.Email.Password = _secretProtector.UnprotectSettingsValue(Settings.Email.Password);
                Settings.WebDav.Password = _secretProtector.UnprotectSettingsValue(Settings.WebDav.Password);
                if (!string.IsNullOrEmpty(Settings.System.PostgreSqlPassword) &&
                    !_secretProtector.IsProtectedPayload(Settings.System.PostgreSqlPassword))
                {
                    throw new InvalidDataException(
                        $"PostgreSQL 密码不能以明文保存在 appsettings.json；请使用 {DbHelper.PostgreSqlPasswordEnvironmentVariable}、" +
                        $"{DbHelper.PostgreSqlPasswordFileEnvironmentVariable}，或通过设置界面保存受保护载荷。");
                }
                Settings.System.PostgreSqlPassword = _secretProtector.UnprotectSettingsValue(Settings.System.PostgreSqlPassword);
                Settings.AI.ApiKey = _secretProtector.UnprotectSettingsValue(Settings.AI.ApiKey);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Settings = previous;
                throw new InvalidDataException($"设置文件包含无效配置：{_filePath}。{ex.Message}", ex);
            }
        }

        public async Task SaveAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                EnsureSettingsDefaults();
                await SaveUnsafeAsync();
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task<bool> UpdateAsync(Func<AppSettings, bool> update, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);

            await _saveLock.WaitAsync(cancellationToken);
            try
            {
                EnsureSettingsDefaults();
                if (!update(Settings))
                {
                    return false;
                }

                await SaveUnsafeAsync();
                return true;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private async Task SaveUnsafeAsync()
        {
            string originalEmailPwd = Settings.Email?.Password ?? string.Empty;
            string originalWebDavPwd = Settings.WebDav?.Password ?? string.Empty;
            string originalPostgreSqlPwd = Settings.System?.PostgreSqlPassword ?? string.Empty;
            string originalAiApiKey = Settings.AI?.ApiKey ?? string.Empty;

            try
            {
                if (!string.IsNullOrEmpty(Settings.Email?.Password))
                {
                    Settings.Email.Password = _secretProtector.Protect(Settings.Email.Password);
                }

                if (!string.IsNullOrEmpty(Settings.WebDav?.Password))
                {
                    Settings.WebDav.Password = _secretProtector.Protect(Settings.WebDav.Password);
                }

                if (!string.IsNullOrEmpty(Settings.System?.PostgreSqlPassword))
                {
                    Settings.System.PostgreSqlPassword = _secretProtector.Protect(Settings.System.PostgreSqlPassword);
                }

                if (!string.IsNullOrEmpty(Settings.AI?.ApiKey))
                {
                    Settings.AI.ApiKey = _secretProtector.Protect(Settings.AI.ApiKey);
                }

                var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                await AtomicFileHelper.WriteAllTextAtomicAsync(_filePath, json);
            }
            finally
            {
                if (Settings.Email != null)
                {
                    Settings.Email.Password = originalEmailPwd;
                }

                if (Settings.WebDav != null)
                {
                    Settings.WebDav.Password = originalWebDavPwd;
                }

                if (Settings.System != null)
                {
                    Settings.System.PostgreSqlPassword = originalPostgreSqlPwd;
                }

                if (Settings.AI != null)
                {
                    Settings.AI.ApiKey = originalAiApiKey;
                }
            }
        }

        private void EnsureSettingsDefaults()
        {
            Settings ??= new AppSettings();
            Settings.System ??= new SystemSettings();
            Settings.BatchExport ??= new BatchExportSettings();
            Settings.PaymentTemplates ??= new List<PaymentTemplateItem>();
            foreach (var paymentTemplate in Settings.PaymentTemplates.Where(item => item != null))
            {
                paymentTemplate.ReportType = ReportDocumentType.PaymentVoucher.ToString();
            }
            Settings.ExcelImport ??= new ExcelImportSettings();
            Settings.ExcelImportSchemes ??= new List<ExcelImportSettings>();
            Settings.ExchangeRate ??= new ExchangeRateSettings();
            Settings.ExchangeRate.SelectedCurrencies ??= new List<string>();
            Settings.ExchangeRate.AllSupportedCurrencies ??= new List<string>();
            Settings.Email ??= new EmailConfig();
            Settings.WebDav ??= new WebDavSettings();
            Settings.AI ??= new AISettings();
            Settings.SingleWindow ??= new SingleWindowSettings();
            Settings.SingleWindow.CustomsCooDefaults ??= new CustomsCooDefaultProfile();

            if (Settings.System.BackupRetentionDays < 0)
            {
                Settings.System.BackupRetentionDays = 0;
            }

            Settings.System.PostgreSqlAutoBackupSchedule =
                string.Equals(Settings.System.PostgreSqlAutoBackupSchedule?.Trim(), "Weekly", StringComparison.OrdinalIgnoreCase)
                    ? "Weekly"
                    : "Daily";
            Settings.System.PostgreSqlAutoBackupTime = TimeSpan.TryParse(
                Settings.System.PostgreSqlAutoBackupTime?.Trim(),
                out var backupTime)
                    ? new TimeSpan(backupTime.Hours, backupTime.Minutes, 0).ToString(@"hh\:mm")
                    : "02:00";
            Settings.System.PostgreSqlAutoBackupDayOfWeek =
                Math.Clamp(Settings.System.PostgreSqlAutoBackupDayOfWeek, 0, 6);
            Settings.System.PostgreSqlAutoBackupRetentionCount =
                Math.Max(1, Settings.System.PostgreSqlAutoBackupRetentionCount);

            if (Settings.System.ItemEntryBlankRowCount <= 0)
            {
                Settings.System.ItemEntryBlankRowCount = 20;
            }
            else if (Settings.System.ItemEntryBlankRowCount > 500)
            {
                Settings.System.ItemEntryBlankRowCount = 500;
            }

            Settings.System.DatabaseProvider = DatabaseModeHelper.NormalizeProvider(Settings.System.DatabaseProvider);

            Settings.System.SqliteDatabaseFileName =
                DbHelper.NormalizeRuntimeSqliteDatabaseFileName(Settings.System.SqliteDatabaseFileName);

            Settings.System.PostgreSqlPort = DbHelper.NormalizePostgreSqlPort(Settings.System.PostgreSqlPort);
            Settings.System.PostgreSqlHost = DbHelper.NormalizePostgreSqlText(Settings.System.PostgreSqlHost);
            Settings.System.PostgreSqlDatabase = DbHelper.NormalizePostgreSqlText(Settings.System.PostgreSqlDatabase);
            Settings.System.PostgreSqlUsername = DbHelper.NormalizePostgreSqlText(Settings.System.PostgreSqlUsername);
            Settings.System.PostgreSqlAdditionalOptions = DbHelper.NormalizePostgreSqlAdditionalOptions(Settings.System.PostgreSqlAdditionalOptions);
            Settings.System.UpdaterEndpoint = UpdaterEndpointPolicy.Normalize(Settings.System.UpdaterEndpoint);
        }

        private static string ResolveSettingsPath(IAppPathProvider pathProvider, string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Path.Combine(pathProvider.ConfigRoot, SettingsFileName);
            }

            var trimmed = filePath.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(pathProvider.ConfigRoot, trimmed));
        }
    }
}

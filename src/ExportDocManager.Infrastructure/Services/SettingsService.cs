using System.Text.Json;
using System.Threading;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Security;
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

        public SettingsService()
            : this(new RuntimeAppPathProvider())
        {
        }

        public SettingsService(string filePath)
            : this(new RuntimeAppPathProvider(), filePath)
        {
        }

        public SettingsService(IAppPathProvider pathProvider)
            : this(pathProvider, null)
        {
        }

        public SettingsService(IAppPathProvider pathProvider, string filePath)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            _filePath = ResolveSettingsPath(pathProvider, filePath);
            _secretProtector = new LocalSecretProtector(pathProvider);
        }

        public async Task LoadAsync()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_filePath);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    EnsureSettingsDefaults();
                    var ep = Settings.Email?.Password;
                    if (!string.IsNullOrEmpty(ep))
                    {
                        var dec = _secretProtector.Unprotect(ep);
                        if (!string.IsNullOrEmpty(dec)) Settings.Email.Password = dec;
                    }
                    var wp = Settings.WebDav?.Password;
                    if (!string.IsNullOrEmpty(wp))
                    {
                        var dec2 = _secretProtector.Unprotect(wp);
                        if (!string.IsNullOrEmpty(dec2)) Settings.WebDav.Password = dec2;
                    }

                    var dp = Settings.System?.PostgreSqlPassword;
                    if (!string.IsNullOrEmpty(dp))
                    {
                        var dec3 = _secretProtector.Unprotect(dp);
                        if (!string.IsNullOrEmpty(dec3)) Settings.System.PostgreSqlPassword = dec3;
                    }

                    var aiKey = Settings.AI?.ApiKey;
                    if (!string.IsNullOrEmpty(aiKey))
                    {
                        var dec4 = _secretProtector.Unprotect(aiKey);
                        if (!string.IsNullOrEmpty(dec4)) Settings.AI.ApiKey = dec4;
                    }
                }
                catch
                {
                    Settings = new AppSettings();
                    EnsureSettingsDefaults();
                }
            }
            else
            {
                EnsureSettingsDefaults();
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
            var originalEmailPwd = Settings.Email?.Password;
            var originalWebDavPwd = Settings.WebDav?.Password;
            var originalPostgreSqlPwd = Settings.System?.PostgreSqlPassword;
            var originalAiApiKey = Settings.AI?.ApiKey;

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
            Settings.PaymentTemplates ??= new List<BatchExportItem>();
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

            if (string.IsNullOrWhiteSpace(Settings.System.SqliteDatabaseFileName))
            {
                Settings.System.SqliteDatabaseFileName = DatabaseConnectionSettings.DefaultSqliteDatabaseFileName;
            }

            Settings.System.PostgreSqlPort = DbHelper.NormalizePostgreSqlPort(Settings.System.PostgreSqlPort);
            Settings.System.PostgreSqlHost = DbHelper.NormalizePostgreSqlText(Settings.System.PostgreSqlHost);
            Settings.System.PostgreSqlDatabase = DbHelper.NormalizePostgreSqlText(Settings.System.PostgreSqlDatabase);
            Settings.System.PostgreSqlUsername = DbHelper.NormalizePostgreSqlText(Settings.System.PostgreSqlUsername);
            Settings.System.PostgreSqlAdditionalOptions = DbHelper.NormalizePostgreSqlAdditionalOptions(Settings.System.PostgreSqlAdditionalOptions);
        }

        private static string ResolveSettingsPath(IAppPathProvider pathProvider, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Path.Combine(pathProvider.AppRoot, SettingsFileName);
            }

            var trimmed = filePath.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(pathProvider.AppRoot, trimmed));
        }
    }
}

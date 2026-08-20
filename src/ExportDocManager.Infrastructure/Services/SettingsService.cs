using System.Text.Json;
using System.Globalization;
using System.Threading;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class SettingsService : ISettingsService
    {
        private const string SettingsFileName = "appsettings.json";
        private static readonly JsonSerializerOptions SnapshotSerializerOptions = new();
        private static readonly JsonSerializerOptions PersistedSerializerOptions = new() { WriteIndented = true };

        private readonly string _filePath;
        private readonly LocalSecretProtector _secretProtector;
        private readonly SemaphoreSlim _mutationGate = new(1, 1);
        private AppSettings _settings = Normalize(new AppSettings());

        public AppSettings Settings => Clone(Volatile.Read(ref _settings));

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

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_filePath))
                {
                    Volatile.Write(ref _settings, Normalize(new AppSettings()));
                    return;
                }

                string json;
                try
                {
                    json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidDataException($"无法读取设置文件 {_filePath}。", ex);
                }

                AppSettings loaded;
                try
                {
                    loaded = JsonSerializer.Deserialize<AppSettings>(json, SnapshotSerializerOptions)
                        ?? throw new InvalidDataException("设置文件内容为空。");
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"设置文件 JSON 损坏：{_filePath}。", ex);
                }

                try
                {
                    loaded = Normalize(loaded);
                    UnprotectSecrets(loaded);
                    Volatile.Write(ref _settings, loaded);
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    throw new InvalidDataException($"设置文件包含无效配置：{_filePath}。{ex.Message}", ex);
                }
            }
            finally
            {
                _mutationGate.Release();
            }
        }

        public async Task<bool> UpdateAsync(
            Func<AppSettings, bool> update,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);

            await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var candidate = Clone(Volatile.Read(ref _settings));
                if (!update(candidate))
                {
                    return false;
                }

                candidate = Normalize(candidate);
                await SaveUnsafeAsync(candidate, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _settings, candidate);
                return true;
            }
            finally
            {
                _mutationGate.Release();
            }
        }

        private async Task SaveUnsafeAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            var persisted = Clone(settings);
            ProtectSecrets(persisted);
            string json = JsonSerializer.Serialize(persisted, PersistedSerializerOptions);
            await AtomicFileHelper.WriteAllTextAtomicAsync(
                    _filePath,
                    json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private void ProtectSecrets(AppSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.Email.Password))
            {
                settings.Email.Password = _secretProtector.Protect(settings.Email.Password);
            }

            if (!string.IsNullOrEmpty(settings.WebDav.Password))
            {
                settings.WebDav.Password = _secretProtector.Protect(settings.WebDav.Password);
            }

            if (!string.IsNullOrEmpty(settings.System.PostgreSqlPassword))
            {
                settings.System.PostgreSqlPassword = _secretProtector.Protect(settings.System.PostgreSqlPassword);
            }

            if (!string.IsNullOrEmpty(settings.AI.ApiKey))
            {
                settings.AI.ApiKey = _secretProtector.Protect(settings.AI.ApiKey);
            }
        }

        private void UnprotectSecrets(AppSettings settings)
        {
            settings.Email.Password = _secretProtector.UnprotectSettingsValue(settings.Email.Password);
            settings.WebDav.Password = _secretProtector.UnprotectSettingsValue(settings.WebDav.Password);
            if (!string.IsNullOrEmpty(settings.System.PostgreSqlPassword) &&
                !_secretProtector.IsProtectedPayload(settings.System.PostgreSqlPassword))
            {
                throw new InvalidDataException(
                    $"PostgreSQL 密码不能以明文保存在 appsettings.json；请使用 {DbHelper.PostgreSqlPasswordEnvironmentVariable}、" +
                    $"{DbHelper.PostgreSqlPasswordFileEnvironmentVariable}，或通过设置界面保存受保护载荷。");
            }

            settings.System.PostgreSqlPassword = _secretProtector.UnprotectSettingsValue(settings.System.PostgreSqlPassword);
            settings.AI.ApiKey = _secretProtector.UnprotectSettingsValue(settings.AI.ApiKey);
        }

        private static AppSettings Normalize(AppSettings? settings)
        {
            settings ??= new AppSettings();
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
            settings.WebDav ??= new WebDavSettings();
            settings.AI ??= new AISettings();
            settings.SingleWindow ??= new SingleWindowSettings();
            settings.SingleWindow.CustomsCooDefaults ??= new CustomsCooDefaultProfile();

            settings.System.BackupRetentionDays = Math.Max(0, settings.System.BackupRetentionDays);
            settings.System.PostgreSqlAutoBackupSchedule =
                string.Equals(settings.System.PostgreSqlAutoBackupSchedule?.Trim(), "Weekly", StringComparison.OrdinalIgnoreCase)
                    ? "Weekly"
                    : "Daily";
            settings.System.PostgreSqlAutoBackupTime = TimeSpan.TryParse(
                settings.System.PostgreSqlAutoBackupTime?.Trim(),
                CultureInfo.InvariantCulture,
                out var backupTime)
                    ? new TimeSpan(backupTime.Hours, backupTime.Minutes, 0).ToString(@"hh\:mm")
                    : "02:00";
            settings.System.PostgreSqlAutoBackupDayOfWeek =
                Math.Clamp(settings.System.PostgreSqlAutoBackupDayOfWeek, 0, 6);
            settings.System.PostgreSqlAutoBackupRetentionCount =
                Math.Max(1, settings.System.PostgreSqlAutoBackupRetentionCount);
            settings.System.ItemEntryBlankRowCount =
                Math.Clamp(settings.System.ItemEntryBlankRowCount <= 0 ? 20 : settings.System.ItemEntryBlankRowCount, 1, 500);
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
            return settings;
        }

        private static AppSettings Clone(AppSettings settings)
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(settings, SnapshotSerializerOptions);
            return JsonSerializer.Deserialize<AppSettings>(json, SnapshotSerializerOptions)
                ?? throw new InvalidOperationException("无法创建设置快照。");
        }

        private static string ResolveSettingsPath(IAppPathProvider pathProvider, string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Path.Combine(pathProvider.ConfigRoot, SettingsFileName);
            }

            string trimmed = filePath.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(pathProvider.ConfigRoot, trimmed));
        }
    }
}

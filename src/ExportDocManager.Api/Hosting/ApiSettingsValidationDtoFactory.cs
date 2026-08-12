using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        private const string ValidationStoragePolicy =
            "设置校验只读取请求体中的设置草稿和当前已保存敏感字段状态，返回内存校验结果与规范化草稿；不保存 appsettings.json、不写数据库、不创建目录、不访问业务数据。";

        public static ApiSettingsValidationResponse ValidateDraft(
            AppSettings requestSettings,
            AppSettings currentSettings,
            bool updateSecrets,
            bool revealLocalPaths = true)
        {
            var raw = CloneSettings(requestSettings ?? new AppSettings());
            var draft = CloneSettings(requestSettings ?? new AppSettings());
            EnsureValidationObjects(raw);
            EnsureValidationObjects(draft);

            var messages = new List<ApiSettingsValidationMessageDto>();
            NormalizeProviderForValidation(draft, raw, messages);
            NormalizeSqliteFileNameForValidation(draft, messages);

            AppSettings prepared;
            try
            {
                prepared = PrepareForSave(draft, currentSettings ?? new AppSettings(), updateSecrets);
            }
            catch (Exception ex)
            {
                messages.Add(Error("settings", ex.Message, isAutoFixable: false));
                prepared = CloneSettings(draft);
                EnsureValidationObjects(prepared);
            }

            AddSystemValidationMessages(raw.System, prepared.System, messages);
            AddEmailValidationMessages(raw.Email, prepared.Email, messages);
            AddWebDavValidationMessages(raw.WebDav, prepared.WebDav, messages);
            AddExchangeRateValidationMessages(raw.ExchangeRate, prepared.ExchangeRate, messages);
            AddAiValidationMessages(raw.AI, prepared.AI, messages);
            AddSingleWindowValidationMessages(raw.SingleWindow, prepared.SingleWindow, messages);

            bool hasErrors = messages.Any(message => IsError(message.Level));
            bool hasWarnings = messages.Any(message => IsWarning(message.Level));
            return new ApiSettingsValidationResponse(
                !hasErrors,
                hasWarnings,
                messages.Any(message => message.IsAutoFixable),
                messages,
                CreateSanitizedSettings(prepared, canManageSettings: true, revealLocalPaths),
                ValidationStoragePolicy);
        }

        private static void EnsureValidationObjects(AppSettings settings)
        {
            settings.System ??= new SystemSettings();
            settings.BatchExport ??= new BatchExportSettings();
            settings.PaymentTemplates ??= new List<PaymentTemplateItem>();
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
        }

        private static void NormalizeProviderForValidation(
            AppSettings draft,
            AppSettings raw,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            string provider = raw.System?.DatabaseProvider ?? string.Empty;
            try
            {
                draft.System.DatabaseProvider = DatabaseModeHelper.NormalizeProvider(provider);
                if (!string.Equals(
                        provider,
                        draft.System.DatabaseProvider,
                        StringComparison.Ordinal))
                {
                    messages.Add(Warning(
                        "system.databaseProvider",
                        $"数据库类型已规范化为 {draft.System.DatabaseProvider}。",
                        isAutoFixable: true));
                }
            }
            catch (ArgumentException)
            {
                draft.System.DatabaseProvider = DatabaseConnectionSettings.SqliteProvider;
                messages.Add(Error(
                    "system.databaseProvider",
                    $"数据库类型 {provider} 不受支持，自动修复会恢复为 SQLite。",
                    isAutoFixable: true));
            }
        }

        private static void NormalizeSqliteFileNameForValidation(
            AppSettings draft,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            try
            {
                draft.System.SqliteDatabaseFileName =
                    DbHelper.NormalizeRuntimeSqliteDatabaseFileName(draft.System.SqliteDatabaseFileName);
            }
            catch (ArgumentException ex)
            {
                draft.System.SqliteDatabaseFileName = DatabaseConnectionSettings.DefaultSqliteDatabaseFileName;
                messages.Add(Error(
                    "system.sqliteDatabaseFileName",
                    $"{ex.Message} 自动修复会恢复为 {DatabaseConnectionSettings.DefaultSqliteDatabaseFileName}。",
                    isAutoFixable: true));
            }
        }

        private static void AddSystemValidationMessages(
            SystemSettings raw,
            SystemSettings normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            raw ??= new SystemSettings();
            normalized ??= new SystemSettings();

            if (string.IsNullOrWhiteSpace(raw.AppName))
            {
                messages.Add(Warning("system.appName", "软件名称为空，保存后主界面标题会缺少名称。", false));
            }

            AddUpdaterEndpointValidationMessages(raw, normalized, messages);

            AddRangeFixMessage(messages, "system.backupRetentionDays", raw.BackupRetentionDays, normalized.BackupRetentionDays, "备份保留天数");
            AddRangeFixMessage(messages, "system.postgreSqlAutoBackupDayOfWeek", raw.PostgreSqlAutoBackupDayOfWeek, normalized.PostgreSqlAutoBackupDayOfWeek, "PostgreSQL 每周备份星期");
            AddRangeFixMessage(messages, "system.postgreSqlAutoBackupRetentionCount", raw.PostgreSqlAutoBackupRetentionCount, normalized.PostgreSqlAutoBackupRetentionCount, "PostgreSQL 备份保留份数");
            AddRangeFixMessage(messages, "system.itemEntryBlankRowCount", raw.ItemEntryBlankRowCount, normalized.ItemEntryBlankRowCount, "商品明细预留空白行数");
            AddRangeFixMessage(messages, "system.auditLogRetentionDays", raw.AuditLogRetentionDays, normalized.AuditLogRetentionDays, "审计日志保留天数");
            AddRangeFixMessage(messages, "system.logRetentionDays", raw.LogRetentionDays, normalized.LogRetentionDays, "文本日志保留天数");
            AddRangeFixMessage(messages, "system.logRetainedFileCount", raw.LogRetainedFileCount, normalized.LogRetainedFileCount, "文本日志保留文件数");
            AddRangeFixMessage(messages, "system.logFileSizeLimitMB", raw.LogFileSizeLimitMB, normalized.LogFileSizeLimitMB, "单个日志文件大小");

            if (!string.Equals(raw.PostgreSqlAutoBackupSchedule?.Trim(), normalized.PostgreSqlAutoBackupSchedule, StringComparison.Ordinal))
            {
                messages.Add(Warning(
                    "system.postgreSqlAutoBackupSchedule",
                    $"PostgreSQL 自动备份周期将规范化为 {normalized.PostgreSqlAutoBackupSchedule}。",
                    true));
            }

            if (!string.Equals(raw.PostgreSqlAutoBackupTime?.Trim(), normalized.PostgreSqlAutoBackupTime, StringComparison.Ordinal))
            {
                messages.Add(Warning(
                    "system.postgreSqlAutoBackupTime",
                    $"PostgreSQL 自动备份时间将规范化为 {normalized.PostgreSqlAutoBackupTime}。",
                    true));
            }

            if (!messages.Any(message => message.PropertyName == "system.sqliteDatabaseFileName" && IsError(message.Level)) &&
                !string.Equals(
                    raw.SqliteDatabaseFileName?.Trim() ?? string.Empty,
                    normalized.SqliteDatabaseFileName ?? string.Empty,
                    StringComparison.Ordinal))
            {
                messages.Add(Warning(
                    "system.sqliteDatabaseFileName",
                    $"SQLite 文件名将规范化为 {normalized.SqliteDatabaseFileName}。",
                    true));
            }

            string databaseValidation = DatabaseModeHelper.Validate(new DatabaseConnectionSettings
            {
                Provider = normalized.DatabaseProvider,
                SqliteDatabaseFileName = normalized.SqliteDatabaseFileName ?? string.Empty,
                PostgreSqlHost = normalized.PostgreSqlHost,
                PostgreSqlPort = normalized.PostgreSqlPort,
                PostgreSqlDatabase = normalized.PostgreSqlDatabase,
                PostgreSqlUsername = normalized.PostgreSqlUsername,
                PostgreSqlPassword = normalized.PostgreSqlPassword,
                PostgreSqlAdditionalOptions = normalized.PostgreSqlAdditionalOptions
            });
            if (!string.IsNullOrWhiteSpace(databaseValidation))
            {
                messages.Add(Error("system.databaseProvider", databaseValidation, false));
            }

            string defaultExportDirectory = normalized.DefaultExportDirectory?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(defaultExportDirectory) &&
                !Directory.Exists(defaultExportDirectory))
            {
                messages.Add(Warning(
                    "system.defaultExportDirectory",
                    "默认导出目录不存在；桌面版会忽略该初始位置，正式输出时仍需重新选择。",
                    false));
            }

        }

        private static void AddUpdaterEndpointValidationMessages(
            SystemSettings raw,
            SystemSettings normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            string rawValue = raw?.UpdaterEndpoint ?? string.Empty;
            string normalizedValue = UpdaterEndpointPolicy.Normalize(normalized?.UpdaterEndpoint);

            if (!string.Equals(rawValue, normalizedValue, StringComparison.Ordinal))
            {
                messages.Add(Warning(
                    "system.updaterEndpoint",
                    "软件更新地址首尾空白将自动移除。",
                    true));
            }

            if (normalizedValue.Length == 0)
            {
                return;
            }

            if (!UpdaterEndpointPolicy.TryValidate(normalizedValue, out _, out string validationError))
            {
                messages.Add(Error("system.updaterEndpoint", validationError, false));
                return;
            }

            if (UpdaterEndpointPolicy.UsesInsecureHttp(normalizedValue))
            {
                messages.Add(Warning(
                    "system.updaterEndpoint",
                    "当前更新地址使用 HTTP。仅应在受控公司内网、专用 VLAN 或可信 VPN 中使用；公网和跨不可信网络仍应使用 HTTPS。安装包签名仍会由客户端内置公钥强制校验。",
                    false));
            }
        }

        private static void AddEmailValidationMessages(
            EmailConfig raw,
            EmailConfig normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            raw ??= new EmailConfig();
            normalized ??= new EmailConfig();
            AddRangeFixMessage(messages, "email.smtpPort", raw.SmtpPort, normalized.SmtpPort, "SMTP 端口");

            bool hasAnySmtp = !string.IsNullOrWhiteSpace(normalized.SmtpHost) ||
                              !string.IsNullOrWhiteSpace(normalized.UserName) ||
                              !string.IsNullOrWhiteSpace(normalized.FromAddress);
            if (hasAnySmtp)
            {
                if (string.IsNullOrWhiteSpace(normalized.SmtpHost))
                {
                    messages.Add(Warning("email.smtpHost", "邮件设置未填写 SMTP 服务器。", false));
                }

                if (string.IsNullOrWhiteSpace(normalized.FromAddress))
                {
                    messages.Add(Warning("email.fromAddress", "邮件设置未填写发件人地址。", false));
                }
            }
        }

        private static void AddWebDavValidationMessages(
            WebDavSettings raw,
            WebDavSettings normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            _ = raw;
            normalized ??= new WebDavSettings();
            if (!normalized.Enabled && string.IsNullOrWhiteSpace(normalized.Url))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(normalized.Url))
            {
                messages.Add(Error("webDav.url", "已启用 WebDAV 备份，但未填写 WebDAV 地址。", false));
            }
            else if (!WebDavEndpointPolicy.TryNormalize(normalized.Url, out _, out string errorMessage))
            {
                messages.Add(Error("webDav.url", errorMessage, false));
            }
        }

        private static void AddExchangeRateValidationMessages(
            ExchangeRateSettings raw,
            ExchangeRateSettings normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            raw ??= new ExchangeRateSettings();
            normalized ??= new ExchangeRateSettings();
            AddRangeFixMessage(messages, "exchangeRate.cacheDurationMinutes", raw.CacheDurationMinutes, normalized.CacheDurationMinutes, "汇率缓存分钟");

            if (string.IsNullOrWhiteSpace(normalized.Url))
            {
                messages.Add(Warning("exchangeRate.url", "汇率源网址为空，今日汇率工具将无法联网刷新。", false));
            }
            else
            {
                try
                {
                    _ = ExchangeRateEndpointPolicy.Normalize(normalized.Url);
                }
                catch (ServiceValidationException ex)
                {
                    messages.Add(Error("exchangeRate.url", ex.Message, false));
                }
            }
        }

        private static void AddAiValidationMessages(
            AISettings raw,
            AISettings normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            _ = raw;
            normalized ??= new AISettings();
            if (!string.IsNullOrWhiteSpace(normalized.ApiEndpoint) &&
                !Uri.TryCreate(normalized.ApiEndpoint.Trim(), UriKind.Absolute, out _))
            {
                messages.Add(Warning("ai.apiEndpoint", "AI API 地址不是有效的绝对地址。", false));
            }
        }

        private static void AddSingleWindowValidationMessages(
            SingleWindowSettings raw,
            SingleWindowSettings normalized,
            ICollection<ApiSettingsValidationMessageDto> messages)
        {
            _ = raw;
            var defaults = normalized?.CustomsCooDefaults ?? new CustomsCooDefaultProfile();
            AddFourDigitWarning(messages, "singleWindow.customsCooDefaults.orgCode", defaults.OrgCode, "签证机构代码");
            AddFourDigitWarning(messages, "singleWindow.customsCooDefaults.fetchPlace", defaults.FetchPlace, "领证机构代码");

            if (!string.IsNullOrWhiteSpace(defaults.AplAdd) &&
                defaults.AplAdd.Trim().Length > 30)
            {
                messages.Add(Warning(
                    "singleWindow.customsCooDefaults.aplAdd",
                    "单一窗口申请地址超过 30 个字符，COO 导出预检可能继续提示超长。",
                    false));
            }
        }

        private static void AddRangeFixMessage(
            ICollection<ApiSettingsValidationMessageDto> messages,
            string propertyName,
            int rawValue,
            int normalizedValue,
            string label)
        {
            if (rawValue == normalizedValue)
            {
                return;
            }

            messages.Add(Warning(
                propertyName,
                $"{label} 将自动规范化为 {normalizedValue}。",
                true));
        }

        private static void AddFourDigitWarning(
            ICollection<ApiSettingsValidationMessageDto> messages,
            string propertyName,
            string value,
            string label)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return;
            }

            if (normalized.Length != 4 || normalized.Any(ch => ch < '0' || ch > '9'))
            {
                messages.Add(Warning(propertyName, $"{label} 应为 4 位数字。", false));
            }
        }

        private static ApiSettingsValidationMessageDto Warning(
            string propertyName,
            string message,
            bool isAutoFixable)
        {
            return new ApiSettingsValidationMessageDto("warning", propertyName, message, isAutoFixable);
        }

        private static ApiSettingsValidationMessageDto Error(
            string propertyName,
            string message,
            bool isAutoFixable)
        {
            return new ApiSettingsValidationMessageDto("error", propertyName, message, isAutoFixable);
        }

        private static bool IsError(string level) =>
            string.Equals(level, "error", StringComparison.OrdinalIgnoreCase);

        private static bool IsWarning(string level) =>
            string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase);
    }
}

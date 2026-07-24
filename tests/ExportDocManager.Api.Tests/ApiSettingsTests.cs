using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;

namespace ExportDocManager.Api.Tests
{
    public class ApiSettingsTests
    {
        [Fact]
        public void SettingsResponse_ShouldRedactSecretsAndExposeSecretState()
        {
            var settings = new AppSettings
            {
                Email = new EmailConfig { Password = "email-secret" },
                WebDav = new WebDavSettings { Password = "webdav-secret" },
                AI = new AISettings { ApiKey = "ai-secret" },
                System = new SystemSettings { PostgreSqlPassword = "pg-secret" }
            };

            var response = ApiSettingsDtoFactory.FromSettings(settings);

            Assert.Equal(string.Empty, response.Settings.Email.Password);
            Assert.Equal(string.Empty, response.Settings.WebDav.Password);
            Assert.Equal(string.Empty, response.Settings.AI.ApiKey);
            Assert.Equal(string.Empty, response.Settings.System.PostgreSqlPassword);
            Assert.True(response.Secrets.EmailPasswordSet);
            Assert.True(response.Secrets.WebDavPasswordSet);
            Assert.True(response.Secrets.AiApiKeySet);
            Assert.True(response.Secrets.PostgreSqlPasswordSet);
            Assert.Equal("email-secret", settings.Email.Password);
            Assert.Contains("appsettings.json", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不会迁入 App_Data", response.StoragePolicy, StringComparison.Ordinal);
        }

        [Fact]
        public void SettingsResponse_ForNonAdmin_ShouldHideDeploymentAndCredentialHints()
        {
            var settings = new AppSettings
            {
                System = new SystemSettings
                {
                    DefaultExportDirectory = @"E:\\Exports",
                    SqliteDatabaseFileName = "team.db",
                    PostgreSqlHost = "postgres.internal",
                    PostgreSqlPort = 6432,
                    PostgreSqlDatabase = "exportdoc",
                    PostgreSqlUsername = "exportdoc_user",
                    PostgreSqlAdditionalOptions = "Ssl Mode=Require"
                },
                Email = new EmailConfig
                {
                    SmtpHost = "smtp.internal",
                    UserName = "sender@example.com",
                    FromAddress = "sender@example.com"
                },
                WebDav = new WebDavSettings
                {
                    Url = "https://dav.internal/",
                    UserName = "backup-user",
                    Enabled = true
                },
                AI = new AISettings
                {
                    ApiEndpoint = "https://ai.internal/v1/chat/completions",
                    ModelName = "private-model",
                    SystemPrompt = "private prompt",
                    ApiKey = "private-key"
                },
                SingleWindow = new SingleWindowSettings
                {
                    CustomsCooDefaults = new CustomsCooDefaultProfile { Applicant = "sensitive" }
                }
            };

            var localResponse = ApiSettingsDtoFactory.FromSettingsForUser(
                settings,
                canManageSettings: false,
                networkMode: false);
            Assert.Equal(@"E:\\Exports", localResponse.Settings.System.DefaultExportDirectory);
            Assert.Equal("data.db", localResponse.Settings.System.SqliteDatabaseFileName);
            Assert.Empty(localResponse.Settings.System.PostgreSqlHost);
            Assert.Equal(5432, localResponse.Settings.System.PostgreSqlPort);
            Assert.Empty(localResponse.Settings.Email.SmtpHost);
            Assert.Empty(localResponse.Settings.WebDav.Url);
            Assert.Empty(localResponse.Settings.AI.ApiEndpoint);
            Assert.Empty(localResponse.Settings.SingleWindow.CustomsCooDefaults.Applicant);
            Assert.False(localResponse.Secrets.AiApiKeySet);

            var networkResponse = ApiSettingsDtoFactory.FromSettingsForUser(
                settings,
                canManageSettings: false,
                networkMode: true);
            Assert.Empty(networkResponse.Settings.System.DefaultExportDirectory);
        }

        [Fact]
        public void SettingsResponse_ShouldPreserveBatchExportItemSelectionAndSealState()
        {
            var settings = new AppSettings
            {
                BatchExport = new BatchExportSettings
                {
                    Items =
                    [
                        new BatchExportItem
                        {
                            Name = "Packing List",
                            TemplatePath = @"Templates\Export\packing_template.html",
                            IsEnabled = false,
                            ShowSeal = false,
                            ReportType = "ExportDocument"
                        }
                    ]
                }
            };

            var response = ApiSettingsDtoFactory.FromSettings(settings);

            var item = Assert.Single(response.Settings.BatchExport.Items);
            Assert.Equal("Packing List", item.Name);
            Assert.Equal(@"Templates\Export\packing_template.html", item.TemplatePath);
            Assert.False(item.IsEnabled);
            Assert.False(item.ShowSeal);
            Assert.Equal("ExportDocument", item.ReportType);
        }

        [Fact]
        public void SettingsResponse_ShouldPreserveBatchExportItemOrder()
        {
            var settings = new AppSettings
            {
                BatchExport = new BatchExportSettings
                {
                    Items =
                    [
                        new BatchExportItem
                        {
                            Name = "Commercial Invoice",
                            TemplatePath = @"Templates\Export\invoice_template.html",
                            ReportType = "ExportDocument"
                        },
                        new BatchExportItem
                        {
                            Name = "Packing List",
                            TemplatePath = @"Templates\Export\packing_template.html",
                            ReportType = "ExportDocument"
                        }
                    ]
                },
                PaymentTemplates =
                [
                    new BatchExportItem
                    {
                        Name = "Payment Request",
                        TemplatePath = @"Templates\Internal\payment_request_template.html",
                        ReportType = "PaymentDocument"
                    },
                    new BatchExportItem
                    {
                        Name = "Expense Reimbursement",
                        TemplatePath = @"Templates\Internal\expense_reimbursement_template.html",
                        ReportType = "PaymentDocument"
                    }
                ]
            };

            var response = ApiSettingsDtoFactory.FromSettings(settings);

            Assert.Collection(
                response.Settings.BatchExport.Items,
                item => Assert.Equal("Commercial Invoice", item.Name),
                item => Assert.Equal("Packing List", item.Name));
            Assert.Collection(
                response.Settings.PaymentTemplates,
                item => Assert.Equal("Payment Request", item.Name),
                item => Assert.Equal("Expense Reimbursement", item.Name));
        }

        [Fact]
        public void SettingsResponse_ShouldExposeDocumentEmailTemplateDefaults()
        {
            var settings = new AppSettings
            {
                Email = new EmailConfig
                {
                    DocumentEmailSubjectTemplate = "Docs {InvoiceNo} {Customer}",
                    DocumentEmailBodyTemplate = "Hello {Customer}, {Date}"
                }
            };

            var response = ApiSettingsDtoFactory.FromSettings(settings);

            Assert.Equal("Docs {InvoiceNo} {Customer}", response.Settings.Email.DocumentEmailSubjectTemplate);
            Assert.Equal("Hello {Customer}, {Date}", response.Settings.Email.DocumentEmailBodyTemplate);
        }

        [Fact]
        public void SettingsResponse_ShouldPreserveExcelImportSchemes()
        {
            var settings = new AppSettings
            {
                ExcelImport = new ExcelImportSettings
                {
                    SchemeName = "Current",
                    InvoiceNoCell = "O9",
                    ItemsStartRow = 20
                },
                ExcelImportSchemes =
                [
                    new ExcelImportSettings
                    {
                        SchemeName = "Factory-A",
                        InvoiceNoCell = "P12",
                        ItemsStartRow = 25,
                        QuantityCol = 11
                    }
                ]
            };

            var response = ApiSettingsDtoFactory.FromSettings(settings);

            Assert.Equal("Current", response.Settings.ExcelImport.SchemeName);
            Assert.Equal("O9", response.Settings.ExcelImport.InvoiceNoCell);
            var scheme = Assert.Single(response.Settings.ExcelImportSchemes);
            Assert.Equal("Factory-A", scheme.SchemeName);
            Assert.Equal("P12", scheme.InvoiceNoCell);
            Assert.Equal(25, scheme.ItemsStartRow);
            Assert.Equal(11, scheme.QuantityCol);
        }

        [Fact]
        public void PrepareForSave_ShouldNormalizeBlankDocumentEmailTemplateDefaults()
        {
            var prepared = ApiSettingsDtoFactory.PrepareForSave(
                new AppSettings
                {
                    Email = new EmailConfig
                    {
                        DocumentEmailSubjectTemplate = "",
                        DocumentEmailBodyTemplate = ""
                    }
                },
                new AppSettings(),
                updateSecrets: false);

            Assert.Equal(EmailConfig.DefaultDocumentEmailSubjectTemplate, prepared.Email.DocumentEmailSubjectTemplate);
            Assert.Equal(EmailConfig.DefaultDocumentEmailBodyTemplate, prepared.Email.DocumentEmailBodyTemplate);
        }

        [Fact]
        public void PrepareForSave_WhenSecretsAreNotUpdated_ShouldPreserveCurrentSecrets()
        {
            var current = new AppSettings
            {
                Email = new EmailConfig { Password = "current-email" },
                WebDav = new WebDavSettings { Password = "current-webdav" },
                AI = new AISettings
                {
                    ApiKey = "current-ai",
                    SystemPrompt = "current prompt"
                },
                System = new SystemSettings { PostgreSqlPassword = "current-pg" }
            };
            var request = new AppSettings
            {
                Email = new EmailConfig { Password = string.Empty },
                WebDav = new WebDavSettings { Password = string.Empty },
                AI = new AISettings
                {
                    ApiKey = string.Empty,
                    SystemPrompt = "new prompt"
                },
                System = new SystemSettings
                {
                    AppName = "Saved",
                    PostgreSqlPassword = string.Empty
                }
            };

            var prepared = ApiSettingsDtoFactory.PrepareForSave(
                request,
                current,
                updateSecrets: false);

            Assert.Equal("Saved", prepared.System.AppName);
            Assert.Equal("current-email", prepared.Email.Password);
            Assert.Equal("current-webdav", prepared.WebDav.Password);
            Assert.Equal("current-ai", prepared.AI.ApiKey);
            Assert.Equal("new prompt", prepared.AI.SystemPrompt);
            Assert.Equal("current-pg", prepared.System.PostgreSqlPassword);
        }

        [Fact]
        public void PrepareForSave_WhenSecretsAreUpdated_ShouldUseRequestSecrets()
        {
            var current = new AppSettings
            {
                Email = new EmailConfig { Password = "current-email" },
                WebDav = new WebDavSettings { Password = "current-webdav" },
                AI = new AISettings { ApiKey = "current-ai" },
                System = new SystemSettings { PostgreSqlPassword = "current-pg" }
            };
            var request = new AppSettings
            {
                Email = new EmailConfig { Password = "new-email" },
                WebDav = new WebDavSettings { Password = "new-webdav" },
                AI = new AISettings { ApiKey = "new-ai" },
                System = new SystemSettings { PostgreSqlPassword = string.Empty }
            };

            var prepared = ApiSettingsDtoFactory.PrepareForSave(
                request,
                current,
                updateSecrets: true);

            Assert.Equal("new-email", prepared.Email.Password);
            Assert.Equal("new-webdav", prepared.WebDav.Password);
            Assert.Equal("new-ai", prepared.AI.ApiKey);
            Assert.Equal(string.Empty, prepared.System.PostgreSqlPassword);
        }

        [Fact]
        public void RequiresRestartForSystemSettingsChange_ShouldDetectDatabaseChanges()
        {
            var current = new SystemSettings
            {
                DatabaseProvider = DatabaseConnectionSettings.SqliteProvider,
                SqliteDatabaseFileName = "data.db"
            };
            var requested = new SystemSettings
            {
                DatabaseProvider = DatabaseConnectionSettings.SqliteProvider,
                SqliteDatabaseFileName = "other.db"
            };

            Assert.True(ApiSettingsDtoFactory.RequiresRestartForSystemSettingsChange(current, requested));
            Assert.False(ApiSettingsDtoFactory.RequiresRestartForSystemSettingsChange(current, current));
        }

        [Fact]
        public void ValidateDraft_ShouldReturnAutoFixableNormalizedSettingsWithoutSavingSecrets()
        {
            var current = new AppSettings
            {
                Email = new EmailConfig { Password = "saved-email-secret" },
                WebDav = new WebDavSettings { Password = "saved-webdav-secret" },
                AI = new AISettings { ApiKey = "saved-ai-secret" },
                System = new SystemSettings { PostgreSqlPassword = "saved-pg-secret" }
            };
            var request = new AppSettings
            {
                System = new SystemSettings
                {
                    DatabaseProvider = "invalid-provider",
                    SqliteDatabaseFileName = "",
                    BackupRetentionDays = -5,
                    PostgreSqlAutoBackupSchedule = "monthly",
                    PostgreSqlAutoBackupTime = "26:61",
                    PostgreSqlAutoBackupDayOfWeek = 9,
                    PostgreSqlAutoBackupRetentionCount = -2,
                    ItemEntryBlankRowCount = 999,
                    LogRetainedFileCount = 0,
                    LogFileSizeLimitMB = 0
                },
                Email = new EmailConfig { Password = "" },
                WebDav = new WebDavSettings { Password = "" },
                AI = new AISettings { ApiKey = "" }
            };

            var response = ApiSettingsDtoFactory.ValidateDraft(
                request,
                current,
                updateSecrets: false);

            Assert.False(response.IsValid);
            Assert.True(response.HasWarnings);
            Assert.True(response.CanAutoFix);
            Assert.Equal(DatabaseConnectionSettings.SqliteProvider, response.NormalizedSettings.System.DatabaseProvider);
            Assert.Equal(DatabaseConnectionSettings.DefaultSqliteDatabaseFileName, response.NormalizedSettings.System.SqliteDatabaseFileName);
            Assert.Equal(0, response.NormalizedSettings.System.BackupRetentionDays);
            Assert.Equal("Daily", response.NormalizedSettings.System.PostgreSqlAutoBackupSchedule);
            Assert.Equal("02:00", response.NormalizedSettings.System.PostgreSqlAutoBackupTime);
            Assert.Equal(6, response.NormalizedSettings.System.PostgreSqlAutoBackupDayOfWeek);
            Assert.Equal(1, response.NormalizedSettings.System.PostgreSqlAutoBackupRetentionCount);
            Assert.Equal(500, response.NormalizedSettings.System.ItemEntryBlankRowCount);
            Assert.Equal(1, response.NormalizedSettings.System.LogRetainedFileCount);
            Assert.Equal(1, response.NormalizedSettings.System.LogFileSizeLimitMB);
            Assert.Equal(string.Empty, response.NormalizedSettings.Email.Password);
            Assert.Equal(string.Empty, response.NormalizedSettings.WebDav.Password);
            Assert.Equal(string.Empty, response.NormalizedSettings.AI.ApiKey);
            Assert.Equal(string.Empty, response.NormalizedSettings.System.PostgreSqlPassword);
            Assert.Contains(response.Messages, message =>
                message.Level == "error" &&
                message.PropertyName == "system.databaseProvider" &&
                message.IsAutoFixable);
            Assert.Contains(response.Messages, message =>
                message.PropertyName == "system.postgreSqlAutoBackupRetentionCount" &&
                message.IsAutoFixable);
            Assert.Contains("不保存 appsettings.json", response.StoragePolicy, StringComparison.Ordinal);
        }

        [Fact]
        public void ValidateDraft_ShouldReportIncompletePostgreSqlAndWebDavConfiguration()
        {
            var request = new AppSettings
            {
                System = new SystemSettings
                {
                    DatabaseProvider = DatabaseConnectionSettings.PostgreSqlProvider,
                    PostgreSqlHost = "127.0.0.1",
                    PostgreSqlDatabase = "",
                    PostgreSqlUsername = ""
                },
                WebDav = new WebDavSettings
                {
                    Enabled = true,
                    Url = "ftp://example.test/backups"
                }
            };

            var response = ApiSettingsDtoFactory.ValidateDraft(
                request,
                new AppSettings(),
                updateSecrets: false);

            Assert.False(response.IsValid);
            Assert.Contains(response.Messages, message =>
                message.Level == "error" &&
                message.PropertyName == "system.databaseProvider" &&
                message.Message.Contains("PostgreSQL", StringComparison.Ordinal));
            Assert.Contains(response.Messages, message =>
                message.Level == "error" &&
                message.PropertyName == "webDav.url" &&
                message.Message.Contains("http", StringComparison.OrdinalIgnoreCase));
            Assert.False(response.Messages.Where(message => message.Level == "error").All(message => message.IsAutoFixable));
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiSettingsIntegrationTests
    {
        [Fact]
        public async Task SettingsEndpoints_ShouldReadForAuthenticatedUsersAndSaveOnlyForAdminsToRuntimeConfigRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-settings",
                "api-settings.db");
            using var anonymousClient = harness.CreateClient();

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            Assert.True(adminLogin.User.Capabilities.CanManageSettings);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var getResponse = await adminClient.GetAsync("/api/settings");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var settingsResponse = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(getResponse);
            Assert.Contains("appsettings.json", settingsResponse.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("Config/appsettings.json", settingsResponse.StoragePolicy, StringComparison.Ordinal);

            var createOperatorResponse = await adminClient.PostAsJsonAsync("/api/users", new
            {
                username = "settings-operator",
                fullName = "Settings Operator",
                role = UserRoleCatalog.User,
                departmentId = string.Empty,
                companyScope = string.Empty,
                isActive = true,
                resetPassword = "operator-pass"
            });
            Assert.Equal(HttpStatusCode.OK, createOperatorResponse.StatusCode);

            var operatorLogin = await harness.LoginAsync(anonymousClient, "settings-operator", "operator-pass");
            Assert.False(operatorLogin.User.Capabilities.CanManageSettings);
            using var operatorClient = harness.CreateClient(operatorLogin.AccessToken);
            var operatorSettingsResponse = await operatorClient.GetAsync("/api/settings");
            Assert.Equal(HttpStatusCode.OK, operatorSettingsResponse.StatusCode);
            var operatorSettings = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(operatorSettingsResponse);
            Assert.Empty(operatorSettings.Settings.System.UpdaterEndpoint);

            string settingsPath = Path.Combine(harness.DataRoot, "Config", "appsettings.json");
            var forbiddenSettings = CloneSettings(settingsResponse.Settings);
            forbiddenSettings.System.AppName = "Blocked Settings Save";
            var forbiddenResponse = await operatorClient.PutAsJsonAsync("/api/settings", new
            {
                settings = forbiddenSettings,
                updateSecrets = false
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
            Assert.False(File.Exists(settingsPath));

            var requestedSettings = CloneSettings(settingsResponse.Settings);
            requestedSettings.System.AppName = "Settings Endpoint Smoke";
            requestedSettings.System.UpdaterEndpoint = "http://updates.internal:8080/desktop/latest.json";
            requestedSettings.System.DefaultExportDirectory = Path.Combine(harness.DataRoot, "Exports", "Configured");
            requestedSettings.ReportTemplateDefaults = new ReportTemplateDefaults
            {
                ExportDocumentTemplatePath = "builtin:Export/invoice_template.html",
                PaymentVoucherTemplatePath = "builtin:Internal/payment_voucher_template.html"
            };
            requestedSettings.BatchExport.Items =
            [
                new BatchExportItem
                {
                    Name = "Smoke Commercial Invoice",
                    TemplatePath = @"Templates\Export\invoice_template.html",
                    ReportType = "ExportDocument",
                    IsEnabled = true,
                    ShowSeal = true
                },
                new BatchExportItem
                {
                    Name = "Smoke Packing List",
                    TemplatePath = @"Templates\Export\packing_template.html",
                    ReportType = "ExportDocument",
                    IsEnabled = false,
                    ShowSeal = false
                }
            ];
            requestedSettings.PaymentTemplates =
            [
                new PaymentTemplateItem
                {
                    Name = "Smoke Payment Request",
                    TemplatePath = @"Templates\Internal\payment_request_template.html",
                    ReportType = "PaymentVoucher",
                    IsEnabled = true
                },
                new PaymentTemplateItem
                {
                    Name = "Smoke Expense Reimbursement",
                    TemplatePath = @"Templates\Internal\expense_reimbursement_template.html",
                    ReportType = "PaymentVoucher",
                    IsEnabled = true
                }
            ];
            var saveResponse = await adminClient.PutAsJsonAsync("/api/settings", new
            {
                settings = requestedSettings,
                updateSecrets = false
            });
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
            var saved = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsSaveResponse>(saveResponse);

            Assert.True(saved.Success);
            Assert.False(saved.RequiresRestart);
            Assert.Equal("Settings Endpoint Smoke", saved.Settings.System.AppName);
            Assert.Equal("http://updates.internal:8080/desktop/latest.json", saved.Settings.System.UpdaterEndpoint);
            Assert.Empty(saved.Settings.System.DefaultExportDirectory);
            Assert.True(File.Exists(settingsPath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Config"),
                Path.GetFullPath(settingsPath),
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(harness.AppRoot, "appsettings.json")));
            string settingsJson = await File.ReadAllTextAsync(settingsPath);
            using (var settingsDocument = JsonDocument.Parse(settingsJson))
            {
                foreach (var item in settingsDocument.RootElement.GetProperty("PaymentTemplates").EnumerateArray())
                {
                    Assert.False(item.TryGetProperty("ShowSeal", out _));
                }
            }
            Assert.Contains("Settings Endpoint Smoke", settingsJson);
            Assert.Contains("http://updates.internal:8080/desktop/latest.json", settingsJson);
            Assert.Contains("Configured", settingsJson, StringComparison.Ordinal);
            Assert.Contains("builtin:Export/invoice_template.html", settingsJson, StringComparison.Ordinal);
            Assert.True(
                settingsJson.IndexOf("Smoke Commercial Invoice", StringComparison.Ordinal) <
                settingsJson.IndexOf("Smoke Packing List", StringComparison.Ordinal));
            Assert.True(
                settingsJson.IndexOf("Smoke Payment Request", StringComparison.Ordinal) <
                settingsJson.IndexOf("Smoke Expense Reimbursement", StringComparison.Ordinal));

            var getAfterSaveResponse = await adminClient.GetAsync("/api/settings");
            Assert.Equal(HttpStatusCode.OK, getAfterSaveResponse.StatusCode);
            var settingsAfterSave = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(getAfterSaveResponse);
            Assert.Equal("Settings Endpoint Smoke", settingsAfterSave.Settings.System.AppName);
            Assert.Equal("http://updates.internal:8080/desktop/latest.json", settingsAfterSave.Settings.System.UpdaterEndpoint);
            Assert.Empty(settingsAfterSave.Settings.System.DefaultExportDirectory);
            Assert.Equal(
                "builtin:Export/invoice_template.html",
                settingsAfterSave.Settings.ReportTemplateDefaults.ExportDocumentTemplatePath);
            Assert.Equal(
                "builtin:Internal/payment_voucher_template.html",
                settingsAfterSave.Settings.ReportTemplateDefaults.PaymentVoucherTemplatePath);
            Assert.Collection(
                settingsAfterSave.Settings.BatchExport.Items,
                item =>
                {
                    Assert.Equal("Smoke Commercial Invoice", item.Name);
                    Assert.True(item.IsEnabled);
                    Assert.True(item.ShowSeal);
                },
                item =>
                {
                    Assert.Equal("Smoke Packing List", item.Name);
                    Assert.False(item.IsEnabled);
                    Assert.False(item.ShowSeal);
                });
            Assert.Collection(
                settingsAfterSave.Settings.PaymentTemplates,
                item =>
                {
                    Assert.Equal("Smoke Payment Request", item.Name);
                    Assert.True(item.IsEnabled);
                    Assert.Equal("PaymentVoucher", item.ReportType);
                },
                item =>
                {
                    Assert.Equal("Smoke Expense Reimbursement", item.Name);
                    Assert.True(item.IsEnabled);
                    Assert.Equal("PaymentVoucher", item.ReportType);
                });

            var invalidSettings = CloneSettings(settingsAfterSave.Settings);
            invalidSettings.System.UpdaterEndpoint = "ftp://updates.example.test/latest.json";
            var invalidSaveResponse = await adminClient.PutAsJsonAsync("/api/settings", new
            {
                settings = invalidSettings,
                updateSecrets = false
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalidSaveResponse.StatusCode);

            var getAfterInvalidSaveResponse = await adminClient.GetAsync("/api/settings");
            var settingsAfterInvalidSave = await ApiIntegrationTestHarness.ReadJsonAsync<ApiSettingsResponse>(getAfterInvalidSaveResponse);
            Assert.Equal(
                "http://updates.internal:8080/desktop/latest.json",
                settingsAfterInvalidSave.Settings.System.UpdaterEndpoint);
        }

        [Fact]
        public async Task UpdateSettings_ShouldRejectSealMetadataInPaymentTemplates()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-settings-payment-seal",
                "api-settings-payment-seal.db");
            using var anonymousClient = harness.CreateClient();
            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            using var request = new StringContent(
                """
                {
                  "settings": {
                    "paymentTemplates": [
                      {
                        "name": "付款模板",
                        "templatePath": "user:Internal/payment.html",
                        "reportType": "PaymentVoucher",
                        "isEnabled": true,
                        "showSeal": true
                      }
                    ]
                  },
                  "updateSecrets": false
                }
                """,
                Encoding.UTF8,
                "application/json");

            var response = await adminClient.PutAsync("/api/settings", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(File.Exists(Path.Combine(harness.DataRoot, "Config", "appsettings.json")));
        }

        private static AppSettings CloneSettings(AppSettings settings)
        {
            return ApiSettingsDtoFactory.PrepareForSave(
                settings,
                new AppSettings(),
                updateSecrets: true);
        }
    }
}

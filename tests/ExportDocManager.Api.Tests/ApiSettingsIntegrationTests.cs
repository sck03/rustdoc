using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiSettingsIntegrationTests
    {
        [Fact]
        public async Task SettingsEndpoints_ShouldReadForAuthenticatedUsersAndSaveOnlyForAdminsToProgramRoot()
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
            Assert.Contains("不会迁入 App_Data", settingsResponse.StoragePolicy, StringComparison.Ordinal);

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

            string settingsPath = Path.Combine(harness.AppRoot, "appsettings.json");
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
                new BatchExportItem
                {
                    Name = "Smoke Payment Request",
                    TemplatePath = @"Templates\Internal\payment_request_template.html",
                    ReportType = "PaymentDocument",
                    IsEnabled = true,
                    ShowSeal = false
                },
                new BatchExportItem
                {
                    Name = "Smoke Expense Reimbursement",
                    TemplatePath = @"Templates\Internal\expense_reimbursement_template.html",
                    ReportType = "PaymentDocument",
                    IsEnabled = true,
                    ShowSeal = true
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
            Assert.True(File.Exists(settingsPath));
            Assert.StartsWith(
                harness.AppRoot,
                Path.GetFullPath(settingsPath),
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(harness.DataRoot, "appsettings.json")));
            string settingsJson = await File.ReadAllTextAsync(settingsPath);
            Assert.Contains("Settings Endpoint Smoke", settingsJson);
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
                    Assert.False(item.ShowSeal);
                },
                item =>
                {
                    Assert.Equal("Smoke Expense Reimbursement", item.Name);
                    Assert.True(item.IsEnabled);
                    Assert.True(item.ShowSeal);
                });
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

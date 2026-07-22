using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Tests
{
    public class ApiSystemAuthAuditEndpointIntegrationTests
    {
        [Fact]
        public async Task SystemAuthAndAuditEndpoints_ShouldPreserveHttpBehaviorAndRuntimeDataRoot()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-system-auth-audit",
                "api-system-auth-audit.db",
                licenseSignatureVerifier: ApiTestLicenseSignatureVerifier.Instance);
            using var anonymousClient = harness.CreateClient();

            var readinessResponse = await anonymousClient.GetAsync("/readyz");
            Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
            using (var readinessDocument = JsonDocument.Parse(await readinessResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal("ok", readinessDocument.RootElement.GetProperty("status").GetString());
            }

            var healthResponse = await anonymousClient.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            using (var healthDocument = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync()))
            {
                var root = healthDocument.RootElement;
                Assert.Equal("ok", root.GetProperty("status").GetString());
                Assert.Equal(string.Empty, root.GetProperty("appRoot").GetString());
                Assert.Equal(string.Empty, root.GetProperty("dataRoot").GetString());
                Assert.Empty(root.GetProperty("runtimePaths").EnumerateArray());
                Assert.Contains("公开健康检查", root.GetProperty("storagePolicy").GetString() ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(@"C:\", root.GetProperty("storagePolicy").GetString() ?? string.Empty);
            }

            var openApiResponse = await anonymousClient.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
            string openApiJson = await openApiResponse.Content.ReadAsStringAsync();
            Assert.Contains("/api/auth/login", openApiJson, StringComparison.Ordinal);
            Assert.Contains("/api/audit-logs", openApiJson, StringComparison.Ordinal);
            Assert.DoesNotContain("/api/system/update", openApiJson, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateInfo", openApiJson, StringComparison.Ordinal);
            Assert.DoesNotContain("UpdateCheckResult", openApiJson, StringComparison.Ordinal);
            Assert.DoesNotContain("StageUpdatePackage", openApiJson, StringComparison.Ordinal);
            Assert.Contains("/api/system/license", openApiJson, StringComparison.Ordinal);

            var anonymousUpdateResponse = await anonymousClient.GetAsync("/api/system/update");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousUpdateResponse.StatusCode);

            var anonymousStageUpdateResponse = await anonymousClient.PostAsync("/api/system/update/stage", content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousStageUpdateResponse.StatusCode);

            var anonymousLicenseResponse = await anonymousClient.GetAsync("/api/system/license");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousLicenseResponse.StatusCode);

            var anonymousRegisterResponse = await anonymousClient.PostAsJsonAsync(
                "/api/system/license/register",
                new { licenseKey = "ignored" });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousRegisterResponse.StatusCode);

            var anonymousMeResponse = await anonymousClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousMeResponse.StatusCode);

            var anonymousAuditResponse = await anonymousClient.GetAsync("/api/audit-logs");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousAuditResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            Assert.Equal("admin", adminLogin.User.Username);
            Assert.Equal("Admin", adminLogin.User.Role);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var adminHealthResponse = await adminClient.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, adminHealthResponse.StatusCode);
            using (var adminHealthDocument = JsonDocument.Parse(await adminHealthResponse.Content.ReadAsStringAsync()))
            {
                var root = adminHealthDocument.RootElement;
                Assert.Equal(Path.GetFullPath(harness.AppRoot), Path.GetFullPath(root.GetProperty("appRoot").GetString() ?? string.Empty));
                Assert.Equal(Path.GetFullPath(harness.DataRoot), Path.GetFullPath(root.GetProperty("dataRoot").GetString() ?? string.Empty));
                Assert.NotEmpty(root.GetProperty("runtimePaths").EnumerateArray());
            }

            Assert.True(File.Exists(harness.DatabasePath));
            Assert.StartsWith(
                Path.Combine(harness.DataRoot, "Database"),
                Path.GetDirectoryName(Path.GetFullPath(harness.DatabasePath)) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var meResponse = await adminClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
            using (var meDocument = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal("admin", meDocument.RootElement.GetProperty("username").GetString());
            }

            var adminUpdateResponse = await adminClient.GetAsync("/api/system/update");
            Assert.Equal(HttpStatusCode.NotFound, adminUpdateResponse.StatusCode);

            var adminCheckUpdateResponse = await adminClient.PostAsync("/api/system/update/check", content: null);
            Assert.Equal(HttpStatusCode.NotFound, adminCheckUpdateResponse.StatusCode);

            var adminStageUpdateResponse = await adminClient.PostAsync("/api/system/update/stage", content: null);
            Assert.Equal(HttpStatusCode.NotFound, adminStageUpdateResponse.StatusCode);

            var licenseStatusResponse = await adminClient.GetAsync("/api/system/license");
            Assert.Equal(HttpStatusCode.OK, licenseStatusResponse.StatusCode);
            using (var licenseStatusDocument = JsonDocument.Parse(await licenseStatusResponse.Content.ReadAsStringAsync()))
            {
                var root = licenseStatusDocument.RootElement;
                Assert.False(root.GetProperty("isRegistered").GetBoolean());
                Assert.Equal(7, root.GetProperty("trialDays").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("machineId").GetString()));
                Assert.StartsWith(
                    Path.Combine(harness.DataRoot, "Security"),
                    Path.GetFullPath(root.GetProperty("licenseStoragePath").GetString() ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(@"C:\", root.GetProperty("storagePolicy").GetString() ?? string.Empty);
            }

            var emptyRegisterResponse = await adminClient.PostAsJsonAsync(
                "/api/system/license/register",
                new { licenseKey = string.Empty });
            Assert.Equal(HttpStatusCode.BadRequest, emptyRegisterResponse.StatusCode);

            var invalidRegisterResponse = await adminClient.PostAsJsonAsync(
                "/api/system/license/register",
                new { licenseKey = "BAD-LICENSE-KEY" });
            Assert.Equal(HttpStatusCode.BadRequest, invalidRegisterResponse.StatusCode);

            await ForceTrialExpiredAsync(harness.DataRoot);

            var expiredLicenseStatusResponse = await adminClient.GetAsync("/api/system/license");
            Assert.Equal(HttpStatusCode.OK, expiredLicenseStatusResponse.StatusCode);
            using (var expiredLicenseDocument = JsonDocument.Parse(await expiredLicenseStatusResponse.Content.ReadAsStringAsync()))
            {
                var root = expiredLicenseDocument.RootElement;
                Assert.False(root.GetProperty("isRegistered").GetBoolean());
                Assert.True(root.GetProperty("isTrialExpired").GetBoolean());
                Assert.Contains("试用期已过", root.GetProperty("message").GetString() ?? string.Empty, StringComparison.Ordinal);
            }

            var meWhileExpiredResponse = await adminClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, meWhileExpiredResponse.StatusCode);

            var blockedAuditResponse = await adminClient.GetAsync("/api/audit-logs?pageNumber=1&pageSize=5");
            Assert.Equal(HttpStatusCode.PaymentRequired, blockedAuditResponse.StatusCode);
            using (var blockedAuditDocument = JsonDocument.Parse(await blockedAuditResponse.Content.ReadAsStringAsync()))
            {
                Assert.Contains(
                    "试用期已过",
                    blockedAuditDocument.RootElement.GetProperty("message").GetString() ?? string.Empty,
                    StringComparison.Ordinal);
            }

            var registerResponse = await adminClient.PostAsJsonAsync(
                "/api/system/license/register",
                new { licenseKey = ApiTestLicenseSignatureVerifier.ValidLicenseKey });
            Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
            using (var registerDocument = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync()))
            {
                var root = registerDocument.RootElement;
                Assert.True(root.GetProperty("success").GetBoolean());
                Assert.Equal("注册成功。", root.GetProperty("message").GetString());
                Assert.True(root.GetProperty("status").GetProperty("isRegistered").GetBoolean());
                Assert.Contains("已注册", root.GetProperty("status").GetProperty("message").GetString() ?? string.Empty, StringComparison.Ordinal);
            }

            var auditResponse = await adminClient.GetAsync("/api/audit-logs?pageNumber=1&pageSize=5");
            Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
            using (var auditDocument = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync()))
            {
                var root = auditDocument.RootElement;
                Assert.Equal(1, root.GetProperty("pageNumber").GetInt32());
                Assert.Equal(5, root.GetProperty("pageSize").GetInt32());
                Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind);
            }

            var logoutResponse = await adminClient.PostAsync("/api/auth/logout", content: null);
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
            using (var logoutDocument = JsonDocument.Parse(await logoutResponse.Content.ReadAsStringAsync()))
            {
                Assert.True(logoutDocument.RootElement.GetProperty("success").GetBoolean());
            }

            var meAfterLogoutResponse = await adminClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogoutResponse.StatusCode);
        }

        private static async Task ForceTrialExpiredAsync(string dataRoot)
        {
            var anchorStore = new FileRuntimeLicenseAnchorStore(
                Path.Combine(dataRoot, "Security", "test-license-anchor.dat"),
                "API 集成测试隔离授权锚点。");
            var anchor = await anchorStore.LoadAsync()
                ?? throw new InvalidOperationException("测试授权锚点尚未创建。");

            anchor.InstallDate = DateTime.Now.AddDays(-8);
            anchor.LastRunDate = DateTime.Now;
            anchor.LicenseKey = string.Empty;
            anchor.LicenseExpireDate = default;
            await anchorStore.SaveAsync(anchor);
        }
    }
}

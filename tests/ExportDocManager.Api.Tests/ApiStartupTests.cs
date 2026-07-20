using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace ExportDocManager.Api.Tests
{
    public class ApiStartupTests
    {
        private static readonly object OcrRuntimeEnvironmentLock = new();

        [Fact]
        public void Parse_ShouldHonorExplicitRuntimePathsAndUrls()
        {
            string appRoot = Path.Combine(Path.GetTempPath(), $"edm-api-app-{Guid.NewGuid():N}");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-data-{Guid.NewGuid():N}");

            var options = ApiRuntimeOptions.Parse(
            [
                "--app-root", appRoot,
                "--data-root", dataRoot,
                "--urls", "http://127.0.0.1:5199",
                "--network-mode", "true",
                "--allowed-origins", "https://erp.example.com;http://192.168.1.20:8080"
            ]);

            Assert.Equal(Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar), options.AppRoot);
            Assert.Equal(Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar), options.DataRoot);
            Assert.Equal("http://127.0.0.1:5199", options.ListenUrls);
            Assert.True(options.NetworkMode);
            Assert.Equal(2, options.AllowedOrigins.Count);
        }

        [Fact]
        public void Validate_ShouldCreateRuntimeDataDirectoriesForRelativeSqliteDatabase()
        {
            string appRoot = CreateTempDirectory("edm-api-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                DbHelper.ConfigurePathProvider(pathProvider);

                var databaseSettings = new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.SqliteProvider,
                    SqliteDatabaseFileName = "api-test.db"
                };

                ApiStartupValidator.Validate(
                    pathProvider,
                    databaseSettings,
                    new ApiRuntimeOptions
                    {
                        AppRoot = appRoot,
                        DataRoot = dataRoot,
                        ListenUrls = "http://127.0.0.1:5199"
                    });

                string expectedDatabaseRoot = Path.Combine(dataRoot, "Database");
                string databasePath = DbHelper.GetDatabasePath(databaseSettings.SqliteDatabaseFileName);

                Assert.True(Directory.Exists(expectedDatabaseRoot));
                Assert.True(Directory.Exists(Path.Combine(dataRoot, "SingleWindow")));
                Assert.StartsWith(expectedDatabaseRoot, databasePath, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void ValidateLocalListenUrls_ShouldRejectLanOrWildcardAddresses()
        {
            Assert.Throws<InvalidOperationException>(
                () => ApiStartupValidator.ValidateLocalListenUrls("http://0.0.0.0:5188"));
            Assert.Throws<InvalidOperationException>(
                () => ApiStartupValidator.ValidateLocalListenUrls("http://*:5188"));
        }

        [Fact]
        public void ValidateListenUrls_ShouldRequireExplicitNetworkModeAndPostgreSql()
        {
            var sqlite = new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.SqliteProvider };
            var postgreSql = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "postgres",
                PostgreSqlDatabase = "exportdoc",
                PostgreSqlUsername = "exportdoc"
            };

            Assert.Throws<InvalidOperationException>(() => ApiStartupValidator.ValidateListenUrls(
                new ApiRuntimeOptions { ListenUrls = "http://0.0.0.0:5188" },
                postgreSql));
            Assert.Throws<InvalidOperationException>(() => ApiStartupValidator.ValidateListenUrls(
                new ApiRuntimeOptions { ListenUrls = "http://0.0.0.0:5188", NetworkMode = true },
                sqlite));

            ApiStartupValidator.ValidateListenUrls(
                new ApiRuntimeOptions { ListenUrls = "http://0.0.0.0:5188", NetworkMode = true },
                postgreSql);
        }

        [Fact]
        public void ApiCorsPolicy_ShouldAllowOnlyLoopbackOrigins()
        {
            Assert.True(ApiCorsPolicy.IsLoopbackOrigin("http://127.0.0.1:5173"));
            Assert.True(ApiCorsPolicy.IsLoopbackOrigin("http://localhost:5173"));
            Assert.True(ApiCorsPolicy.IsLoopbackOrigin("http://tauri.localhost"));
            Assert.True(ApiCorsPolicy.IsLoopbackOrigin("https://[::1]:5173"));
            Assert.True(ApiCorsPolicy.IsLoopbackOrigin("tauri://localhost"));

            Assert.False(ApiCorsPolicy.IsLoopbackOrigin("http://example.localhost"));
            Assert.False(ApiCorsPolicy.IsLoopbackOrigin("http://192.168.1.12:5173"));
            Assert.False(ApiCorsPolicy.IsLoopbackOrigin("https://example.com"));
            Assert.False(ApiCorsPolicy.IsLoopbackOrigin(string.Empty));

            var networkOptions = new ApiRuntimeOptions
            {
                NetworkMode = true,
                AllowedOrigins = ["https://erp.example.com", "http://192.168.1.20:8080"]
            };
            Assert.True(ApiCorsPolicy.IsAllowedOrigin("https://erp.example.com", networkOptions));
            Assert.True(ApiCorsPolicy.IsAllowedOrigin("http://192.168.1.20:8080/path", networkOptions));
            Assert.False(ApiCorsPolicy.IsAllowedOrigin("https://untrusted.example.com", networkOptions));
            Assert.False(ApiCorsPolicy.IsAllowedOrigin(
                "https://erp.example.com",
                new ApiRuntimeOptions { NetworkMode = false, AllowedOrigins = ["https://erp.example.com"] }));
        }

        [Fact]
        public async Task CorsPolicy_ShouldAllowTauriLocalhostPreflight()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-cors",
                "cors-test.db",
                desktopAccessToken: "desktop-secret");
            using var client = harness.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
            request.Headers.TryAddWithoutValidation("Origin", "http://tauri.localhost");
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            request.Headers.TryAddWithoutValidation(
                "Access-Control-Request-Headers",
                ApiDesktopAccessOptions.HeaderName);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
            Assert.Contains("http://tauri.localhost", origins);
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out var headers));
            Assert.Contains(ApiDesktopAccessOptions.HeaderName, string.Join(",", headers), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeHealthEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("\"openapi\":\"3.0.1\"", json, StringComparison.Ordinal);
            Assert.Contains("/healthz", json, StringComparison.Ordinal);
            Assert.Contains("ApiHealthResponse", json, StringComparison.Ordinal);
            Assert.Contains("requirement", json, StringComparison.Ordinal);
            Assert.Contains("core, feature, or optional", json, StringComparison.Ordinal);
            Assert.Contains("runtimeDependencies", json, StringComparison.Ordinal);
            Assert.Contains("ApiRuntimeDependencyInfo", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeBearerSecurityScheme()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("securitySchemes", json, StringComparison.Ordinal);
            Assert.Contains("BearerAuth", json, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"http\"", json, StringComparison.Ordinal);
            Assert.Contains("\"scheme\":\"bearer\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeAuthenticationCapabilitySchema()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/auth/login", json, StringComparison.Ordinal);
            Assert.Contains("/api/auth/me", json, StringComparison.Ordinal);
            Assert.Contains("ApiLoginResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiUserDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiUserCapabilitiesDto", json, StringComparison.Ordinal);
            Assert.Contains("capabilities", json, StringComparison.Ordinal);
            Assert.Contains("canManageSettings", json, StringComparison.Ordinal);
            Assert.Contains("canManageUsers", json, StringComparison.Ordinal);
            Assert.Contains("canViewAllBusinessData", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeDesktopShutdownMaintenanceEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/system/shutdown-maintenance", json, StringComparison.Ordinal);
            Assert.Contains("runShutdownMaintenance", json, StringComparison.Ordinal);
            Assert.Contains("DesktopAccess", json, StringComparison.Ordinal);
            Assert.Contains(ApiDesktopAccessOptions.HeaderName, json, StringComparison.Ordinal);
            Assert.Contains("ApiShutdownMaintenanceResponse", json, StringComparison.Ordinal);
            Assert.Contains("Runtime data root Backups", json, StringComparison.Ordinal);
            Assert.Contains("Program root logs", json, StringComparison.Ordinal);
            Assert.Contains("/api/system/logs/cleanup", json, StringComparison.Ordinal);
            Assert.Contains("cleanupSystemLogs", json, StringComparison.Ordinal);
            Assert.Contains("ApiSystemLogCleanupResponse", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeUserManagementEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/users", json, StringComparison.Ordinal);
            Assert.Contains("/api/users/{id}", json, StringComparison.Ordinal);
            Assert.Contains("listUsers", json, StringComparison.Ordinal);
            Assert.Contains("createUserAccount", json, StringComparison.Ordinal);
            Assert.Contains("updateUserAccount", json, StringComparison.Ordinal);
            Assert.Contains("deleteUserAccount", json, StringComparison.Ordinal);
            Assert.Contains("ApiUserListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiUserAccountDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiUserSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiUserSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("resetPassword", json, StringComparison.Ordinal);
            Assert.Contains("403", json, StringComparison.Ordinal);
            Assert.Contains("409", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeInvoiceListEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/invoices", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/{id}", json, StringComparison.Ordinal);
            Assert.Contains("listInvoices", json, StringComparison.Ordinal);
            Assert.Contains("getInvoice", json, StringComparison.Ordinal);
            Assert.Contains("createInvoice", json, StringComparison.Ordinal);
            Assert.Contains("updateInvoice", json, StringComparison.Ordinal);
            Assert.Contains("deleteInvoice", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/{id}/clone", json, StringComparison.Ordinal);
            Assert.Contains("cloneInvoice", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/{id}/unverify", json, StringComparison.Ordinal);
            Assert.Contains("unverifyInvoice", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/{id}/clone-type", json, StringComparison.Ordinal);
            Assert.Contains("cloneInvoiceAsType", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/shipping-marks/image", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/shipping-marks/image/preview", json, StringComparison.Ordinal);
            Assert.Contains("saveShippingMarkImage", json, StringComparison.Ordinal);
            Assert.Contains("previewShippingMarkImage", json, StringComparison.Ordinal);
            Assert.Contains("/api/invoices/profit-analysis", json, StringComparison.Ordinal);
            Assert.Contains("analyzeInvoiceProfit", json, StringComparison.Ordinal);
            Assert.Contains("ApiPagedResponseOfApiInvoiceListItemDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceListItemDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDetailDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceItemDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceCloneRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceCloneResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceCloneTypeRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceCloneTypeResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiShippingMarkImageSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiShippingMarkImageSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiShippingMarkImagePreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiShippingMarkImagePreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceProfitAnalysisRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceProfitAnalysisResponse", json, StringComparison.Ordinal);
            Assert.Contains("does not read payment/reimbursement data", json, StringComparison.Ordinal);
            Assert.Contains("ApiCommandResponse", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSettingsEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/settings", json, StringComparison.Ordinal);
            Assert.Contains("getSettings", json, StringComparison.Ordinal);
            Assert.Contains("updateSettings", json, StringComparison.Ordinal);
            Assert.Contains("ApiSettingsResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSettingsSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSettingsSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSettingsSecretsDto", json, StringComparison.Ordinal);
            Assert.Contains("403", json, StringComparison.Ordinal);
            Assert.Contains("cannot manage program settings", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeBackupEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/backup", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/cleanup", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/restore", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/cloud/status", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/cloud/test-connection", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/cloud/upload-latest", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/cloud/backups", json, StringComparison.Ordinal);
            Assert.Contains("/api/backup/cloud/download", json, StringComparison.Ordinal);
            Assert.Contains("/api/postgresql-maintenance/backups", json, StringComparison.Ordinal);
            Assert.Contains("/api/postgresql-maintenance/restore-plan", json, StringComparison.Ordinal);
            Assert.Contains("/api/shared-database/ownership", json, StringComparison.Ordinal);
            Assert.Contains("/api/shared-database/ownership/transfer", json, StringComparison.Ordinal);
            Assert.Contains("/api/support-package", json, StringComparison.Ordinal);
            Assert.Contains("listDatabaseBackups", json, StringComparison.Ordinal);
            Assert.Contains("createDatabaseBackup", json, StringComparison.Ordinal);
            Assert.Contains("cleanupDatabaseBackups", json, StringComparison.Ordinal);
            Assert.Contains("restoreDatabaseBackup", json, StringComparison.Ordinal);
            Assert.Contains("getCloudBackupStatus", json, StringComparison.Ordinal);
            Assert.Contains("testCloudBackupConnection", json, StringComparison.Ordinal);
            Assert.Contains("uploadLatestDatabaseBackupToCloud", json, StringComparison.Ordinal);
            Assert.Contains("listCloudDatabaseBackups", json, StringComparison.Ordinal);
            Assert.Contains("downloadCloudDatabaseBackup", json, StringComparison.Ordinal);
            Assert.Contains("listPostgreSqlPhysicalBackups", json, StringComparison.Ordinal);
            Assert.Contains("createPostgreSqlPhysicalBackup", json, StringComparison.Ordinal);
            Assert.Contains("createPostgreSqlRestorePlan", json, StringComparison.Ordinal);
            Assert.Contains("getSharedDatabaseOwnershipSummary", json, StringComparison.Ordinal);
            Assert.Contains("transferSharedDatabaseOwnership", json, StringComparison.Ordinal);
            Assert.Contains("saveSupportPackageToRuntime", json, StringComparison.Ordinal);
            Assert.Contains("downloadSupportPackage", json, StringComparison.Ordinal);
            Assert.Contains("ApiBackupListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiBackupItemDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCloudBackupListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiCloudBackupItemDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCloudBackupDownloadRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiCloudBackupStatusResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiCloudBackupCommandResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiBackupCleanupRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiBackupRestoreRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiPostgreSqlMaintenanceStatusResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiPostgreSqlPhysicalBackupListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiPostgreSqlPhysicalBackupResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiPostgreSqlRestorePlanRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiPostgreSqlRestorePlanResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSharedDatabaseOwnershipSummaryResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSharedDatabaseOwnershipTransferRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSupportPackageRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSupportPackageResponse", json, StringComparison.Ordinal);
            Assert.Contains("runtime data root Backups", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeDashboardEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/dashboard", json, StringComparison.Ordinal);
            Assert.Contains("getDashboard", json, StringComparison.Ordinal);
            Assert.Contains("ApiDashboardResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiDashboardRecentInvoiceDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiDashboardTodoItemDto", json, StringComparison.Ordinal);
            Assert.Contains("does not read payment/reimbursement data", json, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiAuthorizationService_ShouldExposeAdminCapabilities()
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions());
            var user = new User
            {
                Role = UserRoleCatalog.Admin
            };

            var capabilities = service.GetCapabilities(user);

            Assert.True(capabilities.CanManageSettings);
            Assert.True(capabilities.CanManageUsers);
            Assert.True(capabilities.CanViewAllBusinessData);
        }

        [Theory]
        [InlineData(UserRoleCatalog.User)]
        [InlineData(UserRoleCatalog.Finance)]
        [InlineData("")]
        public void ApiAuthorizationService_ShouldRestrictNonAdminManagementCapabilities(string role)
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions());
            var user = new User
            {
                Role = role
            };

            var capabilities = service.GetCapabilities(user);

            Assert.False(capabilities.CanManageSettings);
            Assert.False(capabilities.CanManageUsers);
            Assert.False(capabilities.CanViewAllBusinessData);
        }

        [Fact]
        public void FinancePermissionTemplate_ShouldExposeOnlyFinanceNavigationModulesAndSupportingCapabilities()
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions { ProductEdition = ProductEditionCatalog.Full });
            var user = new User { Role = UserRoleCatalog.Finance };

            var capabilities = service.GetCapabilities(user);

            Assert.Contains(PermissionModuleCatalog.DocumentPayments, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.DocumentQuery, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.DocumentOcr, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.DocumentReports, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.DocumentPaymentReports, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.CommonExchangeRates, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.CommonEmail, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.SystemAbout, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.DocumentCustomOptions, capabilities.EnabledModules);
            Assert.Contains(PermissionModuleCatalog.DocumentReferenceData, capabilities.EnabledModules);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentDashboard, capabilities.EnabledModules);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentInvoices, capabilities.EnabledModules);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentMasterData, capabilities.EnabledModules);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentExcel, capabilities.EnabledModules);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentContainerPacking, capabilities.EnabledModules);
            Assert.DoesNotContain(PermissionModuleCatalog.SalesDashboard, capabilities.EnabledModules);
            Assert.Contains(
                capabilities.ModuleAccess,
                grant => grant.ModuleKey == PermissionModuleCatalog.DocumentReports &&
                         grant.AccessLevel == PermissionAccessLevel.Manage);
        }

        [Fact]
        public void AssignedEmptyTemplate_ShouldNotFallBackToRoleNavigation()
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions { ProductEdition = ProductEditionCatalog.Full });
            var capabilities = service.GetCapabilities(new User
            {
                Role = UserRoleCatalog.User,
                PermissionTemplateId = 99,
                EffectiveModuleAccess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });

            Assert.Empty(capabilities.EnabledModules);
            Assert.Empty(capabilities.ModuleAccess);
            Assert.False(capabilities.CanUseDocumentWorkspace);
        }

        [Fact]
        public void ApiAuthorizationService_ShouldHonorTemplateAccessLevel()
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions { ProductEdition = ProductEditionCatalog.Full });
            var user = new User
            {
                Role = UserRoleCatalog.User,
                EffectiveModuleAccess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [PermissionModuleCatalog.DocumentPayments] = PermissionAccessLevel.View
                }
            };

            Assert.True(service.CanUseModule(user, PermissionModuleCatalog.DocumentPayments, PermissionAccessLevel.View));
            Assert.False(service.CanUseModule(user, PermissionModuleCatalog.DocumentPayments, PermissionAccessLevel.Operate));
            Assert.False(service.CanUseModule(user, PermissionModuleCatalog.DocumentInvoices, PermissionAccessLevel.View));
        }

        [Fact]
        public void ApiUserDtoFactory_ShouldIncludeCapabilities()
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions());
            var user = new User
            {
                Id = 7,
                Username = "admin",
                Role = UserRoleCatalog.Admin,
                IsActive = true
            };

            var dto = ApiUserDtoFactory.FromUser(user, service);

            Assert.Equal(7, dto.Id);
            Assert.True(dto.Capabilities.CanManageSettings);
            Assert.True(dto.Capabilities.CanManageUsers);
            Assert.True(dto.Capabilities.CanViewAllBusinessData);
        }

        [Theory]
        [InlineData("Document", UserRoleCatalog.User, true, false)]
        [InlineData("Document", UserRoleCatalog.Admin, true, false)]
        [InlineData("Document", UserRoleCatalog.Sales, false, false)]
        [InlineData("Sales", UserRoleCatalog.Sales, false, true)]
        [InlineData("Sales", UserRoleCatalog.Admin, false, true)]
        [InlineData("Sales", UserRoleCatalog.User, false, false)]
        [InlineData("Full", UserRoleCatalog.Admin, true, true)]
        [InlineData("Full", UserRoleCatalog.Sales, false, true)]
        [InlineData("Full", UserRoleCatalog.User, true, false)]
        [InlineData("Full", UserRoleCatalog.Finance, true, false)]
        public void ApiAuthorizationService_ShouldIntersectEditionAndRole(
            string edition,
            string role,
            bool expectedDocument,
            bool expectedSales)
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions { ProductEdition = edition });
            var capabilities = service.GetCapabilities(new User { Role = role });

            Assert.Equal(expectedDocument, capabilities.CanUseDocumentWorkspace);
            Assert.Equal(expectedSales, capabilities.CanUseSalesWorkspace);
            Assert.Equal(edition, capabilities.ProductEdition);
        }

        [Theory]
        [InlineData("/api/crm/customers", ProductEditionCatalog.Sales)]
        [InlineData("/api/email-templates", ProductEditionCatalog.Sales)]
        [InlineData("/api/invoices", ProductEditionCatalog.Document)]
        [InlineData("/api/dashboard", ProductEditionCatalog.Document)]
        [InlineData("/api/master-data/customers", ProductEditionCatalog.Document)]
        [InlineData("/api/custom-options/Currency", ProductEditionCatalog.Document)]
        [InlineData("/api/reports/templates", ProductEditionCatalog.Document)]
        public void ApiWorkspaceAccessMiddleware_ShouldClassifyWorkspaceRoutes(string path, string expected)
        {
            Assert.Equal(expected, ApiWorkspaceAccessMiddleware.GetRequiredWorkspace(path));
        }

        [Fact]
        public void ApiWorkspaceAccessMiddleware_ShouldClassifyModuleAndActionAccess()
        {
            Assert.Equal(
                PermissionModuleCatalog.DocumentReferenceData,
                ApiWorkspaceAccessMiddleware.GetRequiredModule("/api/master-data/payees", HttpMethods.Get));
            Assert.Equal(
                PermissionModuleCatalog.DocumentMasterData,
                ApiWorkspaceAccessMiddleware.GetRequiredModule("/api/master-data/payees", HttpMethods.Post));
            Assert.Equal(
                PermissionModuleCatalog.DocumentReferenceData,
                ApiWorkspaceAccessMiddleware.GetRequiredModule("/api/master-data/units", HttpMethods.Get));
            Assert.Equal(
                PermissionModuleCatalog.CommonProductReference,
                ApiWorkspaceAccessMiddleware.GetRequiredModule("/api/master-data/products", HttpMethods.Get));
            Assert.Equal(
                PermissionModuleCatalog.DocumentMasterData,
                ApiWorkspaceAccessMiddleware.GetRequiredModule("/api/master-data/products", HttpMethods.Post));
            Assert.Equal(
                PermissionModuleCatalog.DocumentPaymentReports,
                ApiWorkspaceAccessMiddleware.GetRequiredModule("/api/reports/payments/5/pdf", HttpMethods.Post));
            Assert.Equal(
                PermissionModuleCatalog.DocumentInvoiceReports,
                ApiWorkspaceAccessMiddleware.GetRequiredModule(
                    "/api/reports/templates",
                    HttpMethods.Get,
                    new QueryCollection(new Dictionary<string, StringValues>
                    {
                        ["reportType"] = "ExportDocument"
                    })));
            Assert.Equal(PermissionAccessLevel.View, ApiWorkspaceAccessMiddleware.GetRequiredAccessLevel(HttpMethods.Get));
            Assert.Equal(PermissionAccessLevel.Operate, ApiWorkspaceAccessMiddleware.GetRequiredAccessLevel(HttpMethods.Post));
            Assert.Equal(PermissionAccessLevel.Manage, ApiWorkspaceAccessMiddleware.GetRequiredAccessLevel(HttpMethods.Delete));
        }

        [Theory]
        [InlineData("Document", UserRoleCatalog.Admin, false, false)]
        [InlineData("Sales", UserRoleCatalog.Admin, false, false)]
        [InlineData("Full", UserRoleCatalog.Admin, true, true)]
        [InlineData("Full", UserRoleCatalog.Sales, false, false)]
        public void AdministrativeCapabilities_ShouldRespectProductEdition(
            string edition,
            string role,
            bool expectedUserManagement,
            bool expectedAuditManagement)
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions { ProductEdition = edition });
            var user = new User { Role = role };

            Assert.Equal(expectedUserManagement, service.CanManageUsers(user));
            Assert.Equal(expectedAuditManagement, service.CanManageAuditLogs(user));
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeLetterOfCreditToolEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/letter-of-credit/import", json, StringComparison.Ordinal);
            Assert.Contains("importLetterOfCreditDocument", json, StringComparison.Ordinal);
            Assert.Contains("ApiLetterOfCreditImportRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiLetterOfCreditImportResponse", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/letter-of-credit/review", json, StringComparison.Ordinal);
            Assert.Contains("reviewLetterOfCreditCompliance", json, StringComparison.Ordinal);
            Assert.Contains("ApiLetterOfCreditReviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiLetterOfCreditReviewResponse", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeOcrImageEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/ocr/recognize-image", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/ocr/recognize-image-content", json, StringComparison.Ordinal);
            Assert.Contains("recognizeOcrImage", json, StringComparison.Ordinal);
            Assert.Contains("recognizeOcrImageContent", json, StringComparison.Ordinal);
            Assert.Contains("ApiOcrRecognizeImageRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiOcrRecognizeImageContentRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiOcrRecognizeImageResponse", json, StringComparison.Ordinal);
            Assert.Contains("Clipboard and other pathless sources stay in request memory", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeExcelToolEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/excel/import-preview", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/excel/template/save-to-path", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/excel/template/download", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/excel/booking-sheet/blank/download", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/excel/booking-sheet/convert/upload", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/excel/booking-sheet/from-invoice/{invoiceId}/download", json, StringComparison.Ordinal);
            Assert.Contains("previewExcelImport", json, StringComparison.Ordinal);
            Assert.Contains("startExcelTemplateDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("startBlankBookingSheetDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("uploadAndStartBookingSheetConvertDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("startInvoiceBookingSheetDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("ApiExcelImportPreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiExcelImportPreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiExcelOutputRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiExcelConvertBookingSheetRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceBookingSheetRequest", json, StringComparison.Ordinal);
            Assert.Contains("Resources/ExcelTemplates", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeExchangeRateToolEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/exchange-rates", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/exchange-rates/available-currencies", json, StringComparison.Ordinal);
            Assert.Contains("listExchangeRates", json, StringComparison.Ordinal);
            Assert.Contains("listAvailableExchangeRateCurrencies", json, StringComparison.Ordinal);
            Assert.Contains("ApiExchangeRateListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiExchangeRateAvailableCurrenciesResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiExchangeRateDto", json, StringComparison.Ordinal);
            Assert.Contains("appsettings.json", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeCustomOptionEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/custom-options/{optionType}", json, StringComparison.Ordinal);
            Assert.Contains("listCustomOptions", json, StringComparison.Ordinal);
            Assert.Contains("saveCustomOption", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomOptionListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomOptionSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("CustomOptions", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeEmailToolEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/email/status", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/email/server-suggestion", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/email/send", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/email/test-connection", json, StringComparison.Ordinal);
            Assert.Contains("getEmailToolStatus", json, StringComparison.Ordinal);
            Assert.Contains("suggestEmailServerConfig", json, StringComparison.Ordinal);
            Assert.Contains("sendEmail", json, StringComparison.Ordinal);
            Assert.Contains("testEmailConnection", json, StringComparison.Ordinal);
            Assert.Contains("ApiEmailStatusResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiEmailServerSuggestionRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiEmailServerSuggestionResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiEmailSendRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiEmailSendResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiEmailTestResponse", json, StringComparison.Ordinal);
            Assert.Contains("does not save settings or write files", json, StringComparison.Ordinal);
            Assert.Contains("Explicit attachment file paths", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeContainerPackingAnalyzeEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/container-packing/analyze", json, StringComparison.Ordinal);
            Assert.Contains("analyzeContainerPacking", json, StringComparison.Ordinal);
            Assert.Contains("ApiContainerPackingAnalyzeRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiContainerPackingAnalyzeResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiContainerPackingAnalysisDto", json, StringComparison.Ordinal);
            Assert.Contains("colorArgb", json, StringComparison.Ordinal);
            Assert.Contains("in-memory request data", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/container-packing/projects", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/container-packing/projects/{id}", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/container-packing/container-types", json, StringComparison.Ordinal);
            Assert.Contains("/api/tools/container-packing/container-types/{id}", json, StringComparison.Ordinal);
            Assert.Contains("listContainerPackingProjects", json, StringComparison.Ordinal);
            Assert.Contains("saveContainerPackingProject", json, StringComparison.Ordinal);
            Assert.Contains("deleteContainerPackingProject", json, StringComparison.Ordinal);
            Assert.Contains("listContainerPackingContainerTypes", json, StringComparison.Ordinal);
            Assert.Contains("saveContainerPackingContainerType", json, StringComparison.Ordinal);
            Assert.Contains("deleteContainerPackingContainerType", json, StringComparison.Ordinal);
            Assert.Contains("ApiContainerPackingProjectDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiContainerTypeDto", json, StringComparison.Ordinal);
            Assert.Contains("does not read invoice, customs, payment, or reimbursement data", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeInvoiceReportZipEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/reports/invoices/pdf-zip/save-to-path", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/invoices/pdf-zip/download", json, StringComparison.Ordinal);
            Assert.Contains("startInvoiceReportPdfZipDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceReportZipRequest", json, StringComparison.Ordinal);
            Assert.Contains("BackgroundJobSnapshot", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeInvoiceDocumentPackageEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/reports/invoices/{invoiceId}/document-package/save-to-path", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/invoices/{invoiceId}/document-package/download", json, StringComparison.Ordinal);
            Assert.Contains("startInvoiceDocumentPackageDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentPackageRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentPackageItemRequest", json, StringComparison.Ordinal);
            Assert.Contains("createZip", json, StringComparison.Ordinal);
            Assert.Contains("invoice/customs document domain", json, StringComparison.Ordinal);
            Assert.Contains("batch folder", json, StringComparison.Ordinal);
            Assert.Contains("BackgroundJobSnapshot", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeInvoiceDocumentPackageHtmlPreviewEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/reports/invoices/{invoiceId}/document-package/html-preview", json, StringComparison.Ordinal);
            Assert.Contains("previewInvoiceDocumentPackageHtml", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentPackagePreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentPackagePreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentPackagePreviewItemResponse", json, StringComparison.Ordinal);
            Assert.Contains("returns HTML in memory", json, StringComparison.Ordinal);
            Assert.Contains("does not create PDF, ZIP, cache files, or default export directories", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeInvoiceDocumentEmailEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/reports/invoices/{invoiceId}/document-email", json, StringComparison.Ordinal);
            Assert.Contains("startInvoiceDocumentEmailJob", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentEmailRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDocumentPackageItemRequest", json, StringComparison.Ordinal);
            Assert.Contains("runtime data cache", json, StringComparison.Ordinal);
            Assert.Contains("default attachment directory", json, StringComparison.Ordinal);
            Assert.Contains("BackgroundJobSnapshot", json, StringComparison.Ordinal);
        }

        [Fact]
        public void InvoiceDocumentEmailDefaults_ShouldApplyConfiguredPlaceholders()
        {
            var documentSet = new ApiEndpointRouteBuilderExtensions.ApiInvoiceGeneratedDocumentSet(
                "INV-2026-01",
                "Acme Buyer",
                12,
                new DateTime(2026, 6, 24),
                Array.Empty<string>(),
                Array.Empty<ApiEndpointRouteBuilderExtensions.ApiInvoiceGeneratedDocumentEntry>());
            var config = new EmailConfig
            {
                DocumentEmailSubjectTemplate = "Docs {InvoiceNo} - {Customer} - {Date}",
                DocumentEmailBodyTemplate = "Dear {Customer}, invoice {InvoiceNo} is ready on {Date}."
            };

            string subject = ApiEndpointRouteBuilderExtensions.BuildInvoiceDocumentEmailSubject(
                "",
                config,
                documentSet);
            string body = ApiEndpointRouteBuilderExtensions.BuildInvoiceDocumentEmailBody(
                "",
                config,
                documentSet);
            string manualSubject = ApiEndpointRouteBuilderExtensions.BuildInvoiceDocumentEmailSubject(
                "Manual subject",
                config,
                documentSet);
            string manualBody = ApiEndpointRouteBuilderExtensions.BuildInvoiceDocumentEmailBody(
                "Manual body",
                config,
                documentSet);

            Assert.Equal("Docs INV-2026-01 - Acme Buyer - 20260624", subject);
            Assert.Equal("Dear Acme Buyer, invoice INV-2026-01 is ready on 20260624.", body);
            Assert.Equal("Manual subject", manualSubject);
            Assert.Equal("Manual body", manualBody);
        }

        [Fact]
        public async Task GenerateInvoiceDocumentPdfFilesAsync_ShouldHonorBatchExportFileNamePattern()
        {
            string tempRoot = CreateTempDirectory("edm-api-document-pattern");

            try
            {
                var jobService = new ApiBackgroundJobService();
                var initial = jobService.Upsert(new BackgroundJobSnapshot
                {
                    JobId = "document-pattern-job",
                    Kind = "ReportDocumentPackage",
                    Title = "单据命名规则",
                    Status = BackgroundJobStatusCatalog.Running,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                var jobContext = new ApiBackgroundJobExecutionContext(
                    jobService,
                    initial,
                    CancellationToken.None);

                var services = new ServiceCollection();
                services.AddSingleton<IInvoiceService>(new PatternInvoiceService());
                services.AddSingleton<IReportPdfRenderService, TestReportPdfRenderService>();
                services.AddSingleton<IPdfMergeService, TestPdfMergeService>();
                services.AddSingleton<ISettingsService>(new TestSettingsService(new AppSettings
                {
                    BatchExport = new BatchExportSettings
                    {
                        OutputFileNamePattern = "{Customer}_{DocType}_{InvoiceNo}"
                    }
                }));

                using var provider = services.BuildServiceProvider();
                var documentSet = await ApiEndpointRouteBuilderExtensions.GenerateInvoiceDocumentPdfFilesAsync(
                    provider,
                    jobContext,
                    42,
                    new List<ApiInvoiceDocumentPackageItemRequest>
                    {
                        new()
                        {
                            Name = "Commercial Invoice",
                            ReportType = ReportDocumentType.ExportDocument.ToString(),
                            TemplatePath = "template.html",
                            WithSeal = true
                        }
                    },
                    tempRoot,
                    includeMergedPdf: false,
                    startProgress: 10,
                    endProgress: 82,
                    progressOutputPath: string.Empty);

                var entry = Assert.Single(documentSet.Entries);
                Assert.Equal("Pattern Customer_Commercial Invoice_PATTERN-001.pdf", entry.EntryName);
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(tempRoot, entry.EntryName)),
                    Path.GetFullPath(entry.SourcePath));
                Assert.True(File.Exists(entry.SourcePath));
                Assert.Equal("PATTERN-001", documentSet.InvoiceNo);
                Assert.Equal("Pattern Customer", documentSet.CustomerName);
            }
            finally
            {
                DeleteDirectoryIfExists(tempRoot);
            }
        }

        [Fact]
        public void ValidateInvoiceDocumentPackageRequest_WhenCreateZipFalse_ShouldAcceptExplicitDirectory()
        {
            var request = new ApiInvoiceDocumentPackageRequest
            {
                CreateZip = false,
                IncludeMergedPdf = true,
                DestinationPath = Path.Combine("Reports", "documents-folder.txt"),
                Items =
                [
                    new ApiInvoiceDocumentPackageItemRequest
                    {
                        Name = "Commercial Invoice",
                        ReportType = ReportDocumentType.ExportDocument.ToString(),
                        TemplatePath = "invoice_template.html",
                        WithSeal = true
                    }
                ]
            };

            var result = ApiEndpointRouteBuilderExtensions.ValidateInvoiceDocumentPackageRequest(
                42,
                request,
                out var items,
                out bool includeMergedPdf,
                out bool createZip,
                out string destinationPath);

            Assert.Null(result);
            Assert.Single(items);
            Assert.True(includeMergedPdf);
            Assert.False(createZip);
            Assert.Equal(Path.GetFullPath(request.DestinationPath), destinationPath);
        }

        [Fact]
        public async Task CopyInvoiceDocumentSetToExportFolderAsync_ShouldHonorBatchExportFolderPattern()
        {
            string tempRoot = CreateTempDirectory("edm-api-document-folder-temp");
            string outputRoot = CreateTempDirectory("edm-api-document-folder-output");

            try
            {
                string invoicePath = Path.Combine(tempRoot, "invoice.pdf");
                string packingPath = Path.Combine(tempRoot, "packing.pdf");
                await File.WriteAllTextAsync(invoicePath, "%PDF invoice");
                await File.WriteAllTextAsync(packingPath, "%PDF packing");

                var jobService = new ApiBackgroundJobService();
                var initial = jobService.Upsert(new BackgroundJobSnapshot
                {
                    JobId = "document-folder-job",
                    Kind = "ReportDocumentPackage",
                    Title = "单据文件夹",
                    Status = BackgroundJobStatusCatalog.Running,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                var jobContext = new ApiBackgroundJobExecutionContext(
                    jobService,
                    initial,
                    CancellationToken.None);

                var services = new ServiceCollection();
                services.AddSingleton<ISettingsService>(new TestSettingsService(new AppSettings
                {
                    BatchExport = new BatchExportSettings
                    {
                        OutputFolderPattern = "{InvoiceNo}_{Customer}_{Date}"
                    }
                }));

                using var provider = services.BuildServiceProvider();
                var documentSet = new ApiEndpointRouteBuilderExtensions.ApiInvoiceGeneratedDocumentSet(
                    "PATTERN-001",
                    "Pattern Customer",
                    7,
                    new DateTime(2026, 6, 24),
                    [invoicePath, packingPath],
                    [
                        new ApiEndpointRouteBuilderExtensions.ApiInvoiceGeneratedDocumentEntry(invoicePath, "invoice.pdf"),
                        new ApiEndpointRouteBuilderExtensions.ApiInvoiceGeneratedDocumentEntry(packingPath, "packing.pdf")
                    ]);

                string batchDirectory = await ApiEndpointRouteBuilderExtensions.CopyInvoiceDocumentSetToExportFolderAsync(
                    provider,
                    jobContext,
                    documentSet,
                    outputRoot,
                    82,
                    98);

                Assert.Equal(
                    Path.Combine(outputRoot, "PATTERN-001_Pattern Customer_20260624"),
                    batchDirectory);
                Assert.True(File.Exists(Path.Combine(batchDirectory, "invoice.pdf")));
                Assert.True(File.Exists(Path.Combine(batchDirectory, "packing.pdf")));
                Assert.False(File.Exists(Path.Combine(outputRoot, "documents.zip")));
            }
            finally
            {
                DeleteDirectoryIfExists(tempRoot);
                DeleteDirectoryIfExists(outputRoot);
            }
        }

        [Fact]
        public async Task ApiCompositionRoot_ShouldImportTextLetterOfCreditFromExplicitPath()
        {
            string appRoot = CreateTempDirectory("edm-api-lc-app");
            string dataRoot = CreateTempDirectory("edm-api-lc-data");
            string sourcePath = Path.Combine(dataRoot, "Inputs", "lc.txt");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                await File.WriteAllTextAsync(sourcePath, "LC NO. TEST-001\r\nAMOUNT USD 1000");

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var services = new ServiceCollection();
                services.AddExportDocManagerApiServices(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "api-lc.db"
                    });

                using var provider = services.BuildServiceProvider(validateScopes: true);
                using var scope = provider.CreateScope();

                var documentService = scope.ServiceProvider.GetRequiredService<ILetterOfCreditDocumentService>();
                var result = await documentService.ImportAsync(sourcePath);

                Assert.Equal(Path.GetFullPath(sourcePath), result.SourcePath);
                Assert.Equal("文本文件", result.SourceDescription);
                Assert.Contains("LC NO. TEST-001", result.ExtractedText, StringComparison.Ordinal);
                Assert.False(result.SourcePath.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public async Task ApiCompositionRoot_ShouldPreviewExcelImportFromExplicitPath()
        {
            string appRoot = CreateTempDirectory("edm-api-excel-app");
            string dataRoot = CreateTempDirectory("edm-api-excel-data");
            string sourcePath = Path.Combine(dataRoot, "Inputs", "invoice-import.xlsx");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("明细单");
                    worksheet.Cell("B3").Value = "Exporter Ltd";
                    worksheet.Cell("B8").Value = "Buyer Ltd";
                    worksheet.Cell("O3").Value = "2026-06-23";
                    worksheet.Cell("O9").Value = "INV-XLS-001";
                    worksheet.Cell(20, 3).Value = "STYLE-1";
                    worksheet.Cell(20, 4).Value = "Jacket";
                    worksheet.Cell(20, 10).Value = 12;
                    workbook.SaveAs(sourcePath);
                }

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var services = new ServiceCollection();
                services.AddExportDocManagerApiServices(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "api-excel.db"
                    });

                using var provider = services.BuildServiceProvider(validateScopes: true);
                using var scope = provider.CreateScope();

                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                await settingsService.LoadAsync();
                settingsService.Settings.System.DefaultTemplateExporterNameCn = "宁波测试出口有限公司";
                var excelImportService = scope.ServiceProvider.GetRequiredService<IExcelImportService>();

                var result = await excelImportService.ImportFromExcelAsync(sourcePath);
                var response = ApiExcelDtoFactory.FromImportResult(Path.GetFullPath(sourcePath), result);

                Assert.True(response.Success);
                Assert.Equal(Path.GetFullPath(sourcePath), response.SourcePath);
                Assert.Equal("INV-XLS-001", response.Invoice.InvoiceNo);
                Assert.Equal("Buyer Ltd", response.Customer.CustomerNameEN);
                Assert.Equal("Exporter Ltd", response.Exporter.ExporterNameEN);
                Assert.Equal("宁波测试出口有限公司", response.Exporter.ExporterNameCN);
                Assert.Single(response.Invoice.Items);
                Assert.False(response.SourcePath.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase));
                Assert.Contains("Resources/ExcelTemplates", response.StoragePolicy, StringComparison.Ordinal);
                Assert.DoesNotContain(@"C:\", response.StoragePolicy, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void ApiCompositionRoot_ShouldAnalyzeContainerPackingInMemory()
        {
            string appRoot = CreateTempDirectory("edm-api-packing-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-packing-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var services = new ServiceCollection();
                services.AddExportDocManagerApiServices(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "api-packing.db"
                    });

                using var provider = services.BuildServiceProvider(validateScopes: true);
                var engine = provider.GetRequiredService<IContainerPackingEngine>();
                var request = new ApiContainerPackingAnalyzeRequest
                {
                    Container = new ApiContainerDimensionsDto
                    {
                        Length = 300,
                        Width = 200,
                        Height = 240,
                        Volume = 14.4m,
                        MaxWeight = 10000m
                    },
                    CargoItems =
                    [
                        new ApiContainerPackingCargoInputDto
                        {
                            Name = "样品箱",
                            Length = 100m,
                            Width = 80m,
                            Height = 60m,
                            Weight = 20m,
                            Quantity = 4,
                            ColorArgb = ContainerPackingColor.FromRgb(66, 135, 245).ToArgb(),
                            PreferredZone = nameof(ContainerCargoZone.Auto),
                            LoadSequence = 1
                        }
                    ],
                    Rules = new ApiContainerPackingRulesDto
                    {
                        AllowRotation = true,
                        UsePalletConstraints = false,
                        DefaultPalletLength = 120,
                        DefaultPalletWidth = 100,
                        DefaultPalletHeight = 15,
                        DefaultPalletWeight = 25m,
                        EnforceCenterOfGravity = false,
                        CenterOfGravityTolerancePercent = 20m,
                        MinimumSupportAreaPercent = 70m,
                        RequireSameFootprintStacking = false
                    }
                };

                var packingRequest = ApiContainerPackingDtoFactory.ToRequest(request);
                var response = ApiContainerPackingDtoFactory.FromAnalysis(engine.Analyze(packingRequest));

                Assert.Equal(4, response.Analysis.TotalPackages);
                Assert.Equal(4, response.Analysis.PackedPackages);
                Assert.True(response.Analysis.PackedItems.Count > 0);
                Assert.Contains("不会写入数据库", response.StoragePolicy, StringComparison.Ordinal);
                Assert.DoesNotContain(@"C:\", response.StoragePolicy, StringComparison.OrdinalIgnoreCase);
                Assert.False(Directory.Exists(Path.Combine(dataRoot, "Database")));
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void ApiCompositionRoot_ShouldResolveExcelTemplateFromProgramRootResources()
        {
            string appRoot = CreateTempDirectory("edm-api-template-app");
            string dataRoot = CreateTempDirectory("edm-api-template-data");
            string templatePath = Path.Combine(appRoot, "Resources", "ExcelTemplates", "invoice-import-template.xlsx");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
                using (var workbook = new XLWorkbook())
                {
                    workbook.Worksheets.Add("明细单");
                    workbook.SaveAs(templatePath);
                }

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var services = new ServiceCollection();
                services.AddExportDocManagerApiServices(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "api-template.db"
                    });

                using var provider = services.BuildServiceProvider(validateScopes: true);
                using var scope = provider.CreateScope();

                var templateService = scope.ServiceProvider.GetRequiredService<IExcelImportTemplateService>();
                string resolvedPath = templateService.EnsureDefaultTemplateAvailable();

                Assert.Equal(Path.GetFullPath(templatePath), resolvedPath);
                Assert.StartsWith(Path.Combine(appRoot, "Resources", "ExcelTemplates"), resolvedPath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain($"{Path.DirectorySeparatorChar}App_Data{Path.DirectorySeparatorChar}", resolvedPath);
                Assert.DoesNotContain(dataRoot, resolvedPath, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void OpenApiDocument_ShouldExposePaymentEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/payments", json, StringComparison.Ordinal);
            Assert.Contains("/api/payments/{id}", json, StringComparison.Ordinal);
            Assert.Contains("listPayments", json, StringComparison.Ordinal);
            Assert.Contains("getPayment", json, StringComparison.Ordinal);
            Assert.Contains("createPayment", json, StringComparison.Ordinal);
            Assert.Contains("updatePayment", json, StringComparison.Ordinal);
            Assert.Contains("deletePayment", json, StringComparison.Ordinal);
            Assert.Contains("ApiPaymentDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiPagedResponseOfApiPaymentDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiPaymentSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiCommandResponse", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeAuditLogEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/audit-logs", json, StringComparison.Ordinal);
            Assert.Contains("listAuditLogs", json, StringComparison.Ordinal);
            Assert.Contains("/api/audit-logs/save-to-path", json, StringComparison.Ordinal);
            Assert.Contains("saveAuditLogsToPath", json, StringComparison.Ordinal);
            Assert.Contains("/api/audit-logs/download", json, StringComparison.Ordinal);
            Assert.Contains("downloadAuditLogs", json, StringComparison.Ordinal);
            Assert.Contains("/api/audit-logs/delete", json, StringComparison.Ordinal);
            Assert.Contains("deleteAuditLogsByCriteria", json, StringComparison.Ordinal);
            Assert.Contains("/api/audit-logs/cleanup", json, StringComparison.Ordinal);
            Assert.Contains("cleanupAuditLogs", json, StringComparison.Ordinal);
            Assert.Contains("ApiAuditLogDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiPagedResponseOfApiAuditLogDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiAuditLogPathExportRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiAuditLogCommandResponse", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeJobEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/jobs", json, StringComparison.Ordinal);
            Assert.Contains("/api/jobs/{jobId}", json, StringComparison.Ordinal);
            Assert.Contains("/api/jobs/{jobId}/cancel", json, StringComparison.Ordinal);
            Assert.Contains("/api/jobs/{jobId}/retry", json, StringComparison.Ordinal);
            Assert.Contains("/api/jobs/finished", json, StringComparison.Ordinal);
            Assert.Contains("listJobs", json, StringComparison.Ordinal);
            Assert.Contains("getJob", json, StringComparison.Ordinal);
            Assert.Contains("cancelJob", json, StringComparison.Ordinal);
            Assert.Contains("retryJob", json, StringComparison.Ordinal);
            Assert.Contains("deleteJob", json, StringComparison.Ordinal);
            Assert.Contains("clearFinishedJobs", json, StringComparison.Ordinal);
            Assert.Contains("BackgroundJobSnapshot", json, StringComparison.Ordinal);
            Assert.Contains("ApiPagedResponseOfBackgroundJobSnapshot", json, StringComparison.Ordinal);
            Assert.Contains("canRetry", json, StringComparison.Ordinal);
            Assert.Contains("retryOperation", json, StringComparison.Ordinal);
            Assert.Contains("retryRequestJson", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposePdfMergeJobEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/tools/pdf/merge", json, StringComparison.Ordinal);
            Assert.Contains("startPdfMergeSaveToPathJob", json, StringComparison.Ordinal);
            Assert.Contains("ApiPdfMergeRequest", json, StringComparison.Ordinal);
            Assert.Contains("sourceFiles", json, StringComparison.Ordinal);
            Assert.Contains("destinationPath", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeReportHtmlPreviewEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/reports/templates", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/templates/storage-check", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/templates/content", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/templates/fields", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/invoices/{invoiceId}/html-preview", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/invoices/draft/html-preview", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/payments/{paymentId}/html-preview", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/payments/draft/html-preview", json, StringComparison.Ordinal);
            Assert.Contains("listReportTemplates", json, StringComparison.Ordinal);
            Assert.Contains("createReportTemplate", json, StringComparison.Ordinal);
            Assert.Contains("getReportTemplateFieldCatalog", json, StringComparison.Ordinal);
            Assert.Contains("getReportTemplateContent", json, StringComparison.Ordinal);
            Assert.Contains("saveReportTemplateContent", json, StringComparison.Ordinal);
            Assert.Contains("renameReportTemplate", json, StringComparison.Ordinal);
            Assert.Contains("deleteReportTemplate", json, StringComparison.Ordinal);
            Assert.Contains("saveReportTemplatePackageToPath", json, StringComparison.Ordinal);
            Assert.Contains("importReportTemplatePackage", json, StringComparison.Ordinal);
            Assert.Contains("downloadReportTemplatePackage", json, StringComparison.Ordinal);
            Assert.Contains("uploadReportTemplatePackage", json, StringComparison.Ordinal);
            Assert.Contains("previewReportTemplateContent", json, StringComparison.Ordinal);
            Assert.Contains("previewInvoiceReportHtml", json, StringComparison.Ordinal);
            Assert.Contains("previewInvoiceReportDraftHtml", json, StringComparison.Ordinal);
            Assert.Contains("previewPaymentVoucherHtml", json, StringComparison.Ordinal);
            Assert.Contains("previewPaymentVoucherDraftHtml", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateContentDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateFieldCatalogResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateFieldDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateCreateRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplateRenameRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplatePackageExportRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplatePackageImportRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplatePreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportTemplatePreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportHtmlPreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiInvoiceDraftReportHtmlPreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiPaymentDraftReportHtmlPreviewRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportHtmlPreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiPaymentReportHtmlPreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("storagePolicy", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeReportPdfJobEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/reports/invoices/{invoiceId}/pdf", json, StringComparison.Ordinal);
            Assert.Contains("/api/reports/payments/{paymentId}/pdf", json, StringComparison.Ordinal);
            Assert.Contains("startInvoiceReportPdfDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("startPaymentVoucherPdfDownloadJob", json, StringComparison.Ordinal);
            Assert.Contains("ApiReportPdfRequest", json, StringComparison.Ordinal);
            Assert.Contains("destinationPath", json, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ApiBackgroundJobService_ShouldReturnEmptyPageAndRejectUnknownCancel()
        {
            var service = new ApiBackgroundJobService();

            var page = await service.QueryAsync(new BackgroundJobQuery());
            bool cancelAccepted = await service.RequestCancelAsync("missing-job");

            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
            Assert.False(cancelAccepted);
        }

        [Fact]
        public async Task ApiBackgroundJobService_ShouldDeleteOnlyTerminalJobs()
        {
            var service = new ApiBackgroundJobService();
            service.Upsert(new BackgroundJobSnapshot
            {
                JobId = "running",
                Title = "运行中",
                Status = BackgroundJobStatusCatalog.Running,
                CreatedAt = DateTimeOffset.UtcNow,
                CanCancel = true
            });
            service.Upsert(new BackgroundJobSnapshot
            {
                JobId = "failed",
                Title = "失败",
                Status = BackgroundJobStatusCatalog.Failed,
                CreatedAt = DateTimeOffset.UtcNow
            });
            service.Upsert(new BackgroundJobSnapshot
            {
                JobId = "succeeded",
                Title = "成功",
                Status = BackgroundJobStatusCatalog.Succeeded,
                CreatedAt = DateTimeOffset.UtcNow
            });

            bool deleteRunning = await service.DeleteAsync("running");
            bool deleteFailed = await service.DeleteAsync("failed");
            int cleared = await service.ClearTerminalAsync();

            Assert.False(deleteRunning);
            Assert.True(deleteFailed);
            Assert.Equal(1, cleared);
            Assert.NotNull(await service.GetAsync("running"));
            Assert.Null(await service.GetAsync("failed"));
            Assert.Null(await service.GetAsync("succeeded"));
        }

        [Fact]
        public async Task ApiBackgroundJobService_ShouldPersistSnapshotsUnderRuntimeCache()
        {
            string appRoot = CreateTempDirectory("edm-job-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-job-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ApiBackgroundJobService(pathProvider);
                var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);

                service.Upsert(new BackgroundJobSnapshot
                {
                    JobId = "persisted-succeeded",
                    Kind = "PdfMerge",
                    Title = "已完成任务",
                    Status = BackgroundJobStatusCatalog.Succeeded,
                    ProgressPercent = 100,
                    StatusText = "已完成",
                    CreatedAt = createdAt,
                    CompletedAt = createdAt.AddMinutes(1),
                    OutputPath = Path.Combine(dataRoot, "Exports", "done.pdf"),
                    CanCancel = false,
                    CanRetry = false,
                    RetryOperation = "StartPdfMergeJob",
                    RetryRequestJson = "{\"sourceFiles\":[\"D:\\\\docs\\\\a.pdf\"],\"destinationPath\":\"D:\\\\docs\\\\done.pdf\"}"
                });
                service.Upsert(new BackgroundJobSnapshot
                {
                    JobId = "persisted-running",
                    Kind = "ReportPdf",
                    Title = "运行中任务",
                    Status = BackgroundJobStatusCatalog.Running,
                    ProgressPercent = 30,
                    StatusText = "运行中",
                    CreatedAt = createdAt.AddMinutes(1),
                    StartedAt = createdAt.AddMinutes(1),
                    CanCancel = true,
                    CanRetry = false,
                    RetryOperation = "StartInvoiceReportPdfJob",
                    RetryRequestJson = "{\"invoiceId\":7,\"body\":{\"reportType\":\"ExportDocument\",\"destinationPath\":\"D:\\\\docs\\\\invoice.pdf\"}}"
                });

                string storePath = Path.Combine(pathProvider.CacheRoot, "BackgroundJobs", "jobs.json");
                Assert.True(File.Exists(storePath));
                Assert.StartsWith(pathProvider.CacheRoot, storePath, StringComparison.OrdinalIgnoreCase);

                var restored = new ApiBackgroundJobService(new RuntimeAppPathProvider(appRoot, dataRoot));
                var succeeded = await restored.GetAsync("persisted-succeeded");
                var interrupted = await restored.GetAsync("persisted-running");
                var failedPage = await restored.QueryAsync(new BackgroundJobQuery
                {
                    Status = BackgroundJobStatusCatalog.Failed
                });

                Assert.NotNull(succeeded);
                Assert.Equal(BackgroundJobStatusCatalog.Succeeded, succeeded.Status);
                Assert.False(succeeded.CanCancel);
                Assert.False(succeeded.CanRetry);
                Assert.Equal("StartPdfMergeJob", succeeded.RetryOperation);
                Assert.Contains("done.pdf", succeeded.RetryRequestJson, StringComparison.Ordinal);
                Assert.NotNull(interrupted);
                Assert.Equal(BackgroundJobStatusCatalog.Failed, interrupted.Status);
                Assert.False(interrupted.CanCancel);
                Assert.True(interrupted.CanRetry);
                Assert.Equal("StartInvoiceReportPdfJob", interrupted.RetryOperation);
                Assert.Contains("\"invoiceId\":7", interrupted.RetryRequestJson, StringComparison.Ordinal);
                Assert.Contains("重启", interrupted.ErrorMessage, StringComparison.Ordinal);
                Assert.Contains(failedPage.Items, job => job.JobId == "persisted-running");
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public async Task ApiBackgroundJobRunner_ShouldCancelRunningJob()
        {
            var jobService = new ApiBackgroundJobService();
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobService,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var job = runner.Enqueue(
                "Test",
                "可取消任务",
                "admin",
                async (_, context) =>
                {
                    context.Report(25, "运行中", "等待取消。");
                    started.SetResult();
                    await Task.Delay(TimeSpan.FromSeconds(30), context.CancellationToken);
                    return string.Empty;
                });

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bool accepted = await jobService.RequestCancelAsync(job.JobId);

            Assert.True(accepted);
            var final = await WaitForJobStatusAsync(jobService, job.JobId, BackgroundJobStatusCatalog.Canceled);
            Assert.Equal(BackgroundJobStatusCatalog.Canceled, final.Status);
            Assert.False(final.CanCancel);
        }

        [Fact]
        public async Task ApiBackgroundJobRunner_ShouldRunWithRequestedUserContext()
        {
            var jobService = new ApiBackgroundJobService();
            var services = new ServiceCollection();
            services.AddSingleton<IUserService>(new StubUserService(new User
            {
                Id = 42,
                Username = "operator-job",
                Role = UserRoleCatalog.User,
                IsActive = true
            }));
            services.AddHttpContextAccessor();
            services.AddSingleton<ApiCurrentUserResolver>();
            services.AddSingleton<IApiSessionTokenService, InMemoryApiSessionTokenService>();
            services.AddSingleton<ICurrentUserContext, ApiCurrentUserContext>();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobService,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance);
            var observedUser = new TaskCompletionSource<User>(TaskCreationOptions.RunContinuationsAsynchronously);

            var job = runner.Enqueue(
                "Test",
                "用户上下文任务",
                "operator-job",
                (scope, _) =>
                {
                    observedUser.SetResult(scope.GetRequiredService<ICurrentUserContext>().CurrentUser);
                    return Task.FromResult(string.Empty);
                });

            var user = await observedUser.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var final = await WaitForJobStatusAsync(jobService, job.JobId, BackgroundJobStatusCatalog.Succeeded);

            Assert.Equal(42, user.Id);
            Assert.Equal("operator-job", user.Username);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, final.Status);
        }

        [Fact]
        public async Task ApiBackgroundJobRunner_ShouldMarkFailedJobRetryableWhenDescriptorExists()
        {
            var jobService = new ApiBackgroundJobService();
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobService,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance);

            var job = runner.Enqueue(
                "PdfMerge",
                "失败任务",
                "admin",
                (_, context) =>
                {
                    context.Report(20, "运行中", "准备失败。");
                    return Task.FromException<string>(new InvalidOperationException("boom"));
                },
                retryOperation: "StartPdfMergeJob",
                retryRequestJson: "{\"sourceFiles\":[\"D:\\\\docs\\\\a.pdf\"],\"destinationPath\":\"D:\\\\docs\\\\merged.pdf\"}");

            var final = await WaitForJobStatusAsync(jobService, job.JobId, BackgroundJobStatusCatalog.Failed);

            Assert.Equal(BackgroundJobStatusCatalog.Failed, final.Status);
            Assert.False(final.CanCancel);
            Assert.True(final.CanRetry);
            Assert.Equal("StartPdfMergeJob", final.RetryOperation);
            Assert.Contains("merged.pdf", final.RetryRequestJson, StringComparison.Ordinal);
            Assert.Contains("boom", final.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ApiBackgroundJobRetryDispatcher_ShouldCreateNewJobFromRetryDescriptor()
        {
            string tempRoot = CreateTempDirectory("edm-retry-dispatcher");
            string sourcePath = Path.Combine(tempRoot, "source.pdf");
            string destinationPath = Path.Combine(tempRoot, "merged.pdf");

            try
            {
                await File.WriteAllTextAsync(sourcePath, "%PDF-1.4");
                var jobService = new ApiBackgroundJobService();
                var services = new ServiceCollection();
                services.AddSingleton<IPdfMergeService, TestPdfMergeService>();
                using var provider = services.BuildServiceProvider();
                var runner = new ApiBackgroundJobRunner(
                    jobService,
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<ApiBackgroundJobRunner>.Instance);
                var dispatcher = new ApiBackgroundJobRetryDispatcher(runner);
                var sourceJob = new BackgroundJobSnapshot
                {
                    JobId = "old-failed-job",
                    Kind = "PdfMerge",
                    Title = "旧失败任务",
                    Status = BackgroundJobStatusCatalog.Failed,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CanRetry = true,
                    RetryOperation = "startPdfMergeJob",
                    RetryRequestJson = JsonSerializer.Serialize(
                        new ApiPdfMergeRequest
                        {
                            SourceFiles = new List<string> { sourcePath },
                            DestinationPath = destinationPath
                        },
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))
                };

                var result = await dispatcher.RetryAsync(
                    sourceJob,
                    "admin",
                    new ThrowingInvoiceService(),
                    CancellationToken.None);
                var response = ReadResult(result);
                var acceptedJob = Assert.IsType<BackgroundJobSnapshot>(response.Value);

                Assert.Equal(StatusCodes.Status202Accepted, response.StatusCode);
                Assert.NotNull(acceptedJob);
                Assert.NotEqual(sourceJob.JobId, acceptedJob.JobId);
                Assert.Equal("PdfMerge", acceptedJob.Kind);
                Assert.Equal("admin", acceptedJob.RequestedBy);
                Assert.Equal("StartPdfMergeJob", acceptedJob.RetryOperation);
                Assert.Contains("merged.pdf", acceptedJob.RetryRequestJson, StringComparison.Ordinal);

                var final = await WaitForJobStatusAsync(
                    jobService,
                    acceptedJob.JobId,
                    BackgroundJobStatusCatalog.Succeeded);
                Assert.Equal(BackgroundJobStatusCatalog.Succeeded, final.Status);
                Assert.Equal(Path.GetFullPath(destinationPath), final.OutputPath);
                Assert.False(final.CanRetry);
                Assert.True(File.Exists(destinationPath));
            }
            finally
            {
                DeleteDirectoryIfExists(tempRoot);
            }
        }

        [Fact]
        public async Task ApiBackgroundJobRetryDispatcher_ShouldRejectJobWithoutRetryDescriptor()
        {
            var jobService = new ApiBackgroundJobService();
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobService,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance);
            var dispatcher = new ApiBackgroundJobRetryDispatcher(runner);
            var sourceJob = new BackgroundJobSnapshot
            {
                JobId = "failed-without-descriptor",
                Kind = "PdfMerge",
                Title = "不可重试任务",
                Status = BackgroundJobStatusCatalog.Failed,
                CreatedAt = DateTimeOffset.UtcNow,
                CanRetry = false
            };

            var result = await dispatcher.RetryAsync(
                sourceJob,
                "admin",
                new ThrowingInvoiceService(),
                CancellationToken.None);
            var response = ReadResult(result);
            var error = Assert.IsType<ApiErrorResponse>(response.Value);

            Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
            Assert.Contains("无法重试", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeMasterDataEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/master-data/customers", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/exporters", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/payees", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/products", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/ports", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/units", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/import-path", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/import-upload", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/search-remote", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/fetch-remote-detail", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/{code}", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/by-id/{id}", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/delete-batch", json, StringComparison.Ordinal);
            Assert.Contains("/api/master-data/hs-codes/clear-all", json, StringComparison.Ordinal);
            Assert.Contains("createCustomer", json, StringComparison.Ordinal);
            Assert.Contains("getCustomer", json, StringComparison.Ordinal);
            Assert.Contains("updateCustomer", json, StringComparison.Ordinal);
            Assert.Contains("deleteCustomer", json, StringComparison.Ordinal);
            Assert.Contains("createExporter", json, StringComparison.Ordinal);
            Assert.Contains("updateExporter", json, StringComparison.Ordinal);
            Assert.Contains("deleteExporter", json, StringComparison.Ordinal);
            Assert.Contains("createPayee", json, StringComparison.Ordinal);
            Assert.Contains("updatePayee", json, StringComparison.Ordinal);
            Assert.Contains("deletePayee", json, StringComparison.Ordinal);
            Assert.Contains("createProduct", json, StringComparison.Ordinal);
            Assert.Contains("updateProduct", json, StringComparison.Ordinal);
            Assert.Contains("deleteProduct", json, StringComparison.Ordinal);
            Assert.Contains("createPort", json, StringComparison.Ordinal);
            Assert.Contains("updatePort", json, StringComparison.Ordinal);
            Assert.Contains("deletePort", json, StringComparison.Ordinal);
            Assert.Contains("createUnit", json, StringComparison.Ordinal);
            Assert.Contains("updateUnit", json, StringComparison.Ordinal);
            Assert.Contains("deleteUnit", json, StringComparison.Ordinal);
            Assert.Contains("createHsCode", json, StringComparison.Ordinal);
            Assert.Contains("importHsCodesFromPath", json, StringComparison.Ordinal);
            Assert.Contains("uploadHsCodesImportFile", json, StringComparison.Ordinal);
            Assert.Contains("searchRemoteHsCodes", json, StringComparison.Ordinal);
            Assert.Contains("fetchRemoteHsCodeDetail", json, StringComparison.Ordinal);
            Assert.Contains("updateHsCode", json, StringComparison.Ordinal);
            Assert.Contains("deleteHsCode", json, StringComparison.Ordinal);
            Assert.Contains("deleteHsCodesBatch", json, StringComparison.Ordinal);
            Assert.Contains("clearAllHsCodes", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomerDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiExporterDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiProductDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiHsCodeBatchDeleteRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiHsCodeClearAllRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiHsCodeImportResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiHsCodeSearchResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiPagedResponseOfApiHsCodeDto", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowOperationCenterEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/operation-center", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/operation-center/{batchId}", json, StringComparison.Ordinal);
            Assert.Contains("listSingleWindowOperationCenter", json, StringComparison.Ordinal);
            Assert.Contains("getSingleWindowOperationCenterDetail", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowOperationCenterPageResult", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowOperationCenterRow", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowOperationCenterDetail", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowOperationCenterPackageRecord", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowOperationCenterReceiptRecord", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowReferenceCatalogEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/reference-catalog", json, StringComparison.Ordinal);
            Assert.Contains("getSingleWindowReferenceCatalog", json, StringComparison.Ordinal);
            Assert.Contains("updateSingleWindowReferenceCatalog", json, StringComparison.Ordinal);
            Assert.Contains("resetSingleWindowReferenceCatalog", json, StringComparison.Ordinal);
            Assert.Contains("importSingleWindowReferenceCatalogJson", json, StringComparison.Ordinal);
            Assert.Contains("previewSingleWindowReferenceCatalogExcelImport", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/issuing-authorities", json, StringComparison.Ordinal);
            Assert.Contains("getCustomsCooIssuingAuthorities", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowReferenceCatalogResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowReferenceCatalogSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowReferenceCatalogSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowReferenceCatalogExcelImportPreviewResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowIssuingAuthorityCatalogResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowIssuingAuthorityOptionDto", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowReferenceCatalogModel", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowReferenceCurrencyEntry", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowReferenceAcdTradeModeEntry", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowCollaborationEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/collaboration", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/collaboration/workstations", json, StringComparison.Ordinal);
            Assert.Contains("listSingleWindowCollaboration", json, StringComparison.Ordinal);
            Assert.Contains("listSingleWindowWorkstations", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowCollaborationPageResult", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowOperationTicketRow", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowWorkstationRow", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowDocumentEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/coo/{invoiceId}", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/{invoiceId}/build-defaults", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/{invoiceId}/locked-fields", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/{invoiceId}/unlock-fields", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/producer-profiles", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/producer-profiles/{id}", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/acd/{invoiceId}", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/acd/{invoiceId}/build-defaults", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/acd/{invoiceId}/locked-fields", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/acd/{invoiceId}/unlock-fields", json, StringComparison.Ordinal);
            Assert.Contains("getCustomsCooDocument", json, StringComparison.Ordinal);
            Assert.Contains("saveCustomsCooDocument", json, StringComparison.Ordinal);
            Assert.Contains("buildCustomsCooDefaults", json, StringComparison.Ordinal);
            Assert.Contains("getCustomsCooLockedFields", json, StringComparison.Ordinal);
            Assert.Contains("unlockCustomsCooFields", json, StringComparison.Ordinal);
            Assert.Contains("listCustomsCooProducerProfiles", json, StringComparison.Ordinal);
            Assert.Contains("createCustomsCooProducerProfile", json, StringComparison.Ordinal);
            Assert.Contains("getCustomsCooProducerProfile", json, StringComparison.Ordinal);
            Assert.Contains("updateCustomsCooProducerProfile", json, StringComparison.Ordinal);
            Assert.Contains("deleteCustomsCooProducerProfile", json, StringComparison.Ordinal);
            Assert.Contains("getAgentConsignmentDocument", json, StringComparison.Ordinal);
            Assert.Contains("saveAgentConsignmentDocument", json, StringComparison.Ordinal);
            Assert.Contains("buildAgentConsignmentDefaults", json, StringComparison.Ordinal);
            Assert.Contains("getAgentConsignmentLockedFields", json, StringComparison.Ordinal);
            Assert.Contains("unlockAgentConsignmentFields", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooDocumentDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooItemDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooNonpartyCorpDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooAttachmentDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooDocumentSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooProducerProfileDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooProducerProfileInputDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooProducerProfileListResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooProducerProfileSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooProducerProfileSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowLockedFieldDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowLockedFieldsResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowUnlockFieldsRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiCustomsCooUnlockFieldsResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiAgentConsignmentDocumentDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiAgentConsignmentDocumentSaveResponse", json, StringComparison.Ordinal);
            Assert.Contains("ApiAgentConsignmentUnlockFieldsResponse", json, StringComparison.Ordinal);
            Assert.Contains("hsCode", json, StringComparison.Ordinal);
            Assert.Contains("ieDate", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowExportReviewEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/export-review/{businessType}/{invoiceId}", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/export-review/{businessType}/{invoiceId}/repair", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/coo/{invoiceId}/export-review", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/acd/{invoiceId}/export-review", json, StringComparison.Ordinal);
            Assert.Contains("getSingleWindowExportReview", json, StringComparison.Ordinal);
            Assert.Contains("buildCustomsCooExportReview", json, StringComparison.Ordinal);
            Assert.Contains("buildAgentConsignmentExportReview", json, StringComparison.Ordinal);
            Assert.Contains("repairSingleWindowExportReviewGroups", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowExportReview", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowExportIssueGroup", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowExportIssue", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowEditorNavigationTarget", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowRepairGroupsRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowRepairGroupsResponse", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowSubmitPackageEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/coo/{invoiceId}/submit-package", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/acd/{invoiceId}/submit-package", json, StringComparison.Ordinal);
            Assert.Contains("downloadCustomsCooSubmitPackage", json, StringComparison.Ordinal);
            Assert.Contains("downloadAgentConsignmentSubmitPackage", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowSubmitPackageRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowHandoffPackageResponse", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowPackageManifest", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowPackageFile", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindow/Outbox", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowPackageImportEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/packages/import", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/receipts/import", json, StringComparison.Ordinal);
            Assert.Contains("importSingleWindowSubmitPackage", json, StringComparison.Ordinal);
            Assert.Contains("importSingleWindowReceiptPackage", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowImportPackageRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowImportedPackageResponse", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowReceiptParseResult", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindow/Inbox", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindow/ReceiptInbox", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowReceiptPackageExportEndpoint()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/receipts/save-package-to-path", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/receipts/download-package", json, StringComparison.Ordinal);
            Assert.Contains("downloadSingleWindowReceiptPackage", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowReceiptPackageExportRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowHandoffPackageResponse", json, StringComparison.Ordinal);
            Assert.Contains("receiptFiles", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindow/Outbox", json, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenApiDocument_ShouldExposeSingleWindowClientBridgeEndpoints()
        {
            var document = OpenApiDocumentFactory.Create(new ApiRuntimeOptions
            {
                ListenUrls = "http://127.0.0.1:5188"
            });

            string json = JsonSerializer.Serialize(document);

            Assert.Contains("/api/single-window/client-profile/default", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/client/dispatch", json, StringComparison.Ordinal);
            Assert.Contains("/api/single-window/client/collect-receipts", json, StringComparison.Ordinal);
            Assert.Contains("getSingleWindowDefaultClientProfile", json, StringComparison.Ordinal);
            Assert.Contains("saveSingleWindowDefaultClientProfile", json, StringComparison.Ordinal);
            Assert.Contains("dispatchSingleWindowBatchToClient", json, StringComparison.Ordinal);
            Assert.Contains("collectSingleWindowClientReceipts", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowClientProfileDto", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowClientProfileSaveRequest", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowClientDispatchRequest", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowClientDispatchResult", json, StringComparison.Ordinal);
            Assert.Contains("ApiSingleWindowReceiptCollectionRequest", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindowReceiptCollectionResult", json, StringComparison.Ordinal);
            Assert.Contains("SingleWindow/Inbox", json, StringComparison.Ordinal);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldMapCustomsCooDocumentWithoutNavigationState()
        {
            var document = new CustomsCooDocument
            {
                Id = 7,
                SourceInvoiceId = 11,
                InvoiceNo = "INV-1",
                ContractNo = "CON-1",
                CertNo = "CERT-1"
            };
            document.Items.Add(new CustomsCooItem
            {
                Id = 3,
                DocumentId = 7,
                SourceItemId = 9,
                SourceStyleNo = "STYLE",
                GNo = 2,
                HSCode = "6201",
                GoodsName = "JACKET"
            });
            document.NonpartyCorps.Add(new CustomsCooNonpartyCorp
            {
                Id = 4,
                DocumentId = 7,
                SortNo = 1,
                EntName = "THIRD PARTY"
            });
            document.Attachments.Add(new CustomsCooAttachment
            {
                Id = 5,
                DocumentId = 7,
                FileName = "invoice.pdf",
                FilePath = "D:\\docs\\invoice.pdf",
                SortOrder = 1,
                FileExistsAtBuild = true
            });

            var dto = ApiSingleWindowDtoFactory.FromCustomsCooDocument(document);
            var roundTrip = ApiSingleWindowDtoFactory.ToCustomsCooDocument(dto, sourceInvoiceId: 99);

            Assert.Equal("INV-1", dto.InvoiceNo);
            Assert.Equal("6201", Assert.Single(dto.Items).HSCode);
            Assert.Equal("THIRD PARTY", Assert.Single(dto.NonpartyCorps).EntName);
            Assert.True(Assert.Single(dto.Attachments).FileExistsAtBuild);
            Assert.Equal(99, roundTrip.SourceInvoiceId);
            Assert.Equal("JACKET", Assert.Single(roundTrip.Items).GoodsName);
            Assert.Null(roundTrip.Items[0].Document);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldMapCustomsCooProducerProfileFields()
        {
            var profile = new CustomsCooProducerProfile
            {
                Id = 9,
                CiqRegNo = "91330200TEST",
                PrdcEtpsName = "Ningbo Maker",
                PrdcEtpsConcEr = "Amy",
                PrdcEtpsTel = "0574-1111",
                Producer = "RCEP producer text",
                ProducerTel = "0574-2222",
                ProducerFax = "0574-3333",
                ProducerEmail = "maker@example.com",
                ProducerSertFlag = "Y",
                LastInvoiceNo = "INV-1",
                LastContractNo = "CON-1",
                LastSourceStyleNo = "STYLE-1",
                CreatedAt = new DateTime(2026, 6, 1, 8, 0, 0),
                UpdatedAt = new DateTime(2026, 6, 2, 9, 0, 0),
                LastUsedAt = new DateTime(2026, 6, 3, 10, 0, 0)
            };

            var dto = ApiSingleWindowDtoFactory.FromCustomsCooProducerProfile(profile);
            var input = ApiSingleWindowDtoFactory.ToCustomsCooProducerProfileInput(dto);
            var response = ApiSingleWindowDtoFactory.FromSavedCustomsCooProducerProfile(profile, "saved");

            Assert.Equal(9, dto.Id);
            Assert.Equal("Ningbo Maker", dto.PrdcEtpsName);
            Assert.Equal("0574-2222", input.ProducerTel);
            Assert.Equal("maker@example.com", input.ProducerEmail);
            Assert.Equal("STYLE-1", input.LastSourceStyleNo);
            Assert.True(response.Success);
            Assert.Contains("CustomsCooProducerProfiles", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不读取付款/报销单据", response.StoragePolicy, StringComparison.Ordinal);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldMapAgentConsignmentDocumentAndForceInvoiceId()
        {
            var document = new AgentConsignmentDocument
            {
                Id = 8,
                SourceInvoiceId = 12,
                InvoiceNo = "INV-2",
                ContractNo = "CON-2",
                GName = "GARMENTS",
                IEDate = "2026-06-23",
                WarningSummary = "warning"
            };

            var dto = ApiSingleWindowDtoFactory.FromAgentConsignmentDocument(document);
            var roundTrip = ApiSingleWindowDtoFactory.ToAgentConsignmentDocument(dto, sourceInvoiceId: 88);

            Assert.Equal("GARMENTS", dto.GName);
            Assert.Equal("2026-06-23", dto.IEDate);
            Assert.Equal(88, roundTrip.SourceInvoiceId);
            Assert.Equal("warning", roundTrip.WarningSummary);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldWrapHandoffPackageResultWithStoragePolicy()
        {
            var response = ApiSingleWindowDtoFactory.FromHandoffPackageResult(
                new SingleWindowHandoffPackageResult
                {
                    PackagePath = "D:\\run\\SingleWindow\\Outbox\\acd-1.swpkg",
                    Manifest = new SingleWindowPackageManifest
                    {
                        BusinessType = SingleWindowBusinessType.AgentConsignment,
                        InvoiceNo = "INV-1"
                    },
                    TrackingBatchId = 12
                },
                "ok");

            Assert.True(response.Success);
            Assert.Equal(12, response.TrackingBatchId);
            Assert.Contains("SingleWindow/Outbox", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("运行数据根 Security", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Equal("ok", response.Message);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldWrapReceiptPackageResultWithStoragePolicy()
        {
            var response = ApiSingleWindowDtoFactory.FromReceiptPackageResult(
                new SingleWindowHandoffPackageResult
                {
                    PackagePath = "D:\\run\\SingleWindow\\Outbox\\receipt-acd.swpkg",
                    Manifest = new SingleWindowPackageManifest
                    {
                        PackageType = SingleWindowPackageType.ReceiptPackage,
                        BusinessType = SingleWindowBusinessType.AgentConsignment,
                        InvoiceNo = "INV-3"
                    },
                    TrackingBatchId = 13
                },
                "receipt ok");

            Assert.True(response.Success);
            Assert.Equal(13, response.TrackingBatchId);
            Assert.Equal(SingleWindowPackageType.ReceiptPackage, response.Manifest.PackageType);
            Assert.Contains("SingleWindow/Outbox", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("回执源文件", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("运行数据根 Security", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Equal("receipt ok", response.Message);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldWrapClientProfileWithStoragePolicy()
        {
            var response = ApiSingleWindowDtoFactory.FromClientProfile(new SwClientProfile
            {
                Id = 3,
                ProfileName = "Profile A",
                MachineName = "MACHINE-A",
                ImportRootPath = "D:\\SingleWindow\\Acd",
                ReceiptRootPath = "D:\\SingleWindow\\Acd",
                CanSubmitAgentConsignment = true,
                CanSubmitCustomsCoo = false,
                IsEnabled = true,
                UpdatedAt = new DateTime(2026, 6, 23)
            });

            Assert.Equal(3, response.Profile.Id);
            Assert.Equal("Profile A", response.Profile.ProfileName);
            Assert.Equal("D:\\SingleWindow\\Acd", response.Profile.ImportRootPath);
            Assert.False(response.Profile.CanSubmitCustomsCoo);
            Assert.Contains("运行目录数据库", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("SingleWindow/Inbox", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("运行数据根 Security", response.StoragePolicy, StringComparison.Ordinal);
        }

        [Fact]
        public void SingleWindowDtoFactory_ShouldWrapImportedPackageResultWithStoragePolicy()
        {
            var response = ApiSingleWindowDtoFactory.FromImportedPackage(
                "D:\\run\\inbox\\submit.swpkg",
                new SingleWindowImportedPackage
                {
                    WorkingDirectory = "D:\\run\\SingleWindow\\Inbox\\sw-import",
                    Manifest = new SingleWindowPackageManifest
                    {
                        PackageType = SingleWindowPackageType.SubmitPackage,
                        BusinessType = SingleWindowBusinessType.AgentConsignment,
                        InvoiceNo = "INV-2"
                    },
                    ParsedReceipts =
                    [
                        new SingleWindowReceiptParseResult
                        {
                            BusinessType = SingleWindowBusinessType.AgentConsignment,
                            ReceiptKind = SingleWindowReceiptKind.AgentConsignmentImportResponse,
                            ReceiptCode = "0",
                            ReceiptMessage = "成功"
                        }
                    ],
                    TrackingBatchId = 8,
                    TrackingStatus = "Accepted",
                    PersistedReceiptCount = 1
                },
                workingDirectoryKept: true,
                "imported");

            Assert.True(response.Success);
            Assert.True(response.WorkingDirectoryKept);
            Assert.Equal(8, response.TrackingBatchId);
            Assert.Equal(1, response.PersistedReceiptCount);
            Assert.Contains("SingleWindow/Inbox", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("ReceiptInbox", response.StoragePolicy, StringComparison.Ordinal);
            Assert.Equal("imported", response.Message);
        }

        [Fact]
        public void ApiServices_ShouldResolveSingleWindowHandoffPackageDependencies()
        {
            string appRoot = CreateTempDirectory("edm-api-di-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-di-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var services = new ServiceCollection();
                services.AddExportDocManagerApiServices(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "api-di.db"
                    });

                using var provider = services.BuildServiceProvider(validateScopes: true);
                using var scope = provider.CreateScope();

                var trackingService = scope.ServiceProvider.GetRequiredService<SingleWindowTrackingService>();
                var trackingPort = scope.ServiceProvider.GetRequiredService<ISingleWindowTrackingService>();
                var handoffService = scope.ServiceProvider.GetRequiredService<ISingleWindowHandoffPackageService>();
                var bridgeService = scope.ServiceProvider.GetRequiredService<ManualImportClientBridge>();
                var profilePort = scope.ServiceProvider.GetRequiredService<ISingleWindowClientProfileService>();
                var bridgePort = scope.ServiceProvider.GetRequiredService<ISingleWindowClientBridge>();
                var pdfMergeService = scope.ServiceProvider.GetRequiredService<IPdfMergeService>();
                var reportHtmlService = scope.ServiceProvider.GetRequiredService<IReportHtmlService>();
                var reportTemplateService = scope.ServiceProvider.GetRequiredService<IReportTemplateService>();
                var reportTemplatePackageService = scope.ServiceProvider.GetRequiredService<IReportTemplatePackageService>();
                var reportTemplateFieldCatalogService = scope.ServiceProvider.GetRequiredService<IReportTemplateFieldCatalogService>();
                var htmlToPdfService = scope.ServiceProvider.GetRequiredService<IHtmlToPdfService>();
                var reportPdfRenderService = scope.ServiceProvider.GetRequiredService<IReportPdfRenderService>();
                var ocrService = scope.ServiceProvider.GetRequiredService<IOcrService>();
                var letterOfCreditDocumentService = scope.ServiceProvider.GetRequiredService<ILetterOfCreditDocumentService>();
                var excelImportService = scope.ServiceProvider.GetRequiredService<IExcelImportService>();
                var excelImportTemplateService = scope.ServiceProvider.GetRequiredService<IExcelImportTemplateService>();
                var containerPackingEngine = scope.ServiceProvider.GetRequiredService<IContainerPackingEngine>();
                var jobRunner = scope.ServiceProvider.GetRequiredService<ApiBackgroundJobRunner>();

                Assert.Same(trackingService, trackingPort);
                Assert.NotNull(handoffService);
                Assert.Same(bridgeService, profilePort);
                Assert.Same(bridgeService, bridgePort);
                Assert.NotNull(pdfMergeService);
                Assert.NotNull(reportHtmlService);
                Assert.NotNull(reportTemplateService);
                Assert.NotNull(reportTemplatePackageService);
                Assert.NotEmpty(reportTemplateFieldCatalogService.GetFieldCatalog(ReportDocumentType.ExportDocument).Fields);
                Assert.NotNull(htmlToPdfService);
                Assert.NotNull(reportPdfRenderService);
                Assert.NotNull(ocrService);
                Assert.NotNull(letterOfCreditDocumentService);
                Assert.NotNull(excelImportService);
                Assert.NotNull(excelImportTemplateService);
                Assert.NotNull(containerPackingEngine);
                Assert.NotNull(jobRunner);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void ApiServices_ShouldUseUnsupportedOcrByDefaultWhenRustSidecarIsMissing()
        {
            lock (OcrRuntimeEnvironmentLock)
            {
                string appRoot = CreateTempDirectory("edm-api-ocr-app");
                string dataRoot = CreateTempDirectory("edm-api-ocr-data");
                string previousRuntime = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");

                try
                {
                    Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", null);
                    var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                    var services = new ServiceCollection();
                    services.AddExportDocManagerApiServices(
                        pathProvider,
                        new DatabaseConnectionSettings
                        {
                            Provider = DatabaseConnectionSettings.SqliteProvider,
                            SqliteDatabaseFileName = "api-ocr.db"
                        });

                    using var provider = services.BuildServiceProvider(validateScopes: true);
                    using var scope = provider.CreateScope();

                    var ocrService = scope.ServiceProvider.GetRequiredService<IOcrService>();

                    Assert.IsType<UnsupportedOcrService>(ocrService);
                    Assert.StartsWith(appRoot, pathProvider.OcrModelRoot, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", previousRuntime);
                    DeleteDirectoryIfExists(appRoot);
                    DeleteDirectoryIfExists(dataRoot);
                }
            }
        }

        [Fact]
        public void ApiServices_ShouldUseRustOcrWhenSidecarIsBundled()
        {
            lock (OcrRuntimeEnvironmentLock)
            {
                string appRoot = CreateTempDirectory("edm-api-ocr-app");
                string dataRoot = CreateTempDirectory("edm-api-ocr-data");
                string previousRuntime = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");

                try
                {
                    Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", null);
                    CreateRustOcrPlaceholders(appRoot);
                    var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                    var services = new ServiceCollection();
                    services.AddExportDocManagerApiServices(
                        pathProvider,
                        new DatabaseConnectionSettings
                        {
                            Provider = DatabaseConnectionSettings.SqliteProvider,
                            SqliteDatabaseFileName = "api-ocr.db"
                        });

                    using var provider = services.BuildServiceProvider(validateScopes: true);
                    using var scope = provider.CreateScope();

                    var ocrService = scope.ServiceProvider.GetRequiredService<IOcrService>();

                    Assert.IsType<RustOcrService>(ocrService);

                    Assert.Equal(Path.Combine(appRoot, "OcrModels"), pathProvider.OcrModelRoot);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", previousRuntime);
                    DeleteDirectoryIfExists(appRoot);
                    DeleteDirectoryIfExists(dataRoot);
                }
            }
        }

        [Fact]
        public void ApiServices_ShouldHonorDisabledOcrRuntimeOverride()
        {
            lock (OcrRuntimeEnvironmentLock)
            {
                string appRoot = CreateTempDirectory("edm-api-ocr-app");
                string dataRoot = CreateTempDirectory("edm-api-ocr-data");
                string previousRuntime = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");

                try
                {
                    Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", "disabled");
                    CreateRustOcrPlaceholders(appRoot);
                    var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                    var services = new ServiceCollection();
                    services.AddExportDocManagerApiServices(
                        pathProvider,
                        new DatabaseConnectionSettings
                        {
                            Provider = DatabaseConnectionSettings.SqliteProvider,
                            SqliteDatabaseFileName = "api-ocr.db"
                        });

                    using var provider = services.BuildServiceProvider(validateScopes: true);
                    using var scope = provider.CreateScope();

                    var ocrService = scope.ServiceProvider.GetRequiredService<IOcrService>();

                    Assert.IsType<UnsupportedOcrService>(ocrService);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME", previousRuntime);
                    DeleteDirectoryIfExists(appRoot);
                    DeleteDirectoryIfExists(dataRoot);
                }
            }
        }

        [Fact]
        public void HealthResponse_ShouldDescribeRuntimeStoragePolicy()
        {
            string appRoot = CreateTempDirectory("edm-api-health-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-health-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var response = ApiHealthResponseFactory.Create(
                    pathProvider,
                    new DatabaseConnectionSettings(),
                    Path.Combine(pathProvider.DatabaseRoot, "exportdoc.db"),
                    new RuntimeDependencyDiagnosticsService(pathProvider).Inspect());
                var publicResponse = ApiHealthResponseFactory.CreatePublic(response);

                Assert.Equal("ok", response.Status);
                Assert.False(string.IsNullOrWhiteSpace(response.ProductVersion));
                Assert.False(string.IsNullOrWhiteSpace(response.InformationalVersion));
                Assert.Equal(appRoot, response.AppRoot);
                Assert.Equal(dataRoot, response.DataRoot);
                Assert.Contains(response.RuntimePaths, item =>
                    item.Key == "template-root" &&
                    item.StorageClass == "program-resource" &&
                    item.AccessMode == "managed" &&
                    item.Requirement == ApiRuntimePathRequirement.Feature &&
                    item.Path == Path.Combine(appRoot, "Templates") &&
                    !item.Exists);
                Assert.Contains(response.RuntimePaths, item =>
                    item.Key == "tool-root" &&
                    item.Requirement == ApiRuntimePathRequirement.Optional &&
                    !item.Exists);
                Assert.Contains(response.RuntimePaths, item =>
                    item.Key == "log-root" &&
                    item.StorageClass == "runtime-data" &&
                    item.AccessMode == "read-write" &&
                    item.Requirement == ApiRuntimePathRequirement.Core &&
                    item.Path == Path.Combine(dataRoot, "Logs") &&
                    item.Exists);
                Assert.Contains(response.RuntimePaths, item =>
                    item.Key == "sqlite-database" &&
                    item.StorageClass == "database-file" &&
                    item.Requirement == ApiRuntimePathRequirement.Core &&
                    item.Path == Path.Combine(dataRoot, "Database", "exportdoc.db"));
                Assert.Contains(response.RuntimeDependencies, item =>
                    item.Key == "report-renderer" &&
                    item.Requirement == ApiRuntimePathRequirement.Feature &&
                    item.Status == "missing" &&
                    !item.Ready);
                Assert.Equal(string.Empty, publicResponse.AppRoot);
                Assert.Equal(string.Empty, publicResponse.DataRoot);
                Assert.Empty(publicResponse.RuntimePaths);
                Assert.Empty(publicResponse.RuntimeDependencies);
                Assert.Contains("公开健康检查", publicResponse.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains(response.RuntimeDependencies, item =>
                    item.Key == "postgresql-tools" &&
                    item.Requirement == ApiRuntimePathRequirement.Optional &&
                    !item.Ready);
                Assert.Contains("appsettings.json", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("Templates/Resources/Browsers/Tools/OcrModels", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("路径只解析、不因健康检查自动创建", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("数据库、日志", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("App_Data", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("--data-root", response.StoragePolicy, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        private static (int StatusCode, object Value) ReadResult(IResult result)
        {
            var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
            var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
            return (statusResult.StatusCode ?? StatusCodes.Status200OK, valueResult.Value);
        }

        private static async Task<BackgroundJobSnapshot> WaitForJobStatusAsync(
            ApiBackgroundJobService service,
            string jobId,
            string expectedStatus)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                var job = await service.GetAsync(jobId);
                if (job != null && string.Equals(job.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return job;
                }

                await Task.Delay(20);
            }

            return await service.GetAsync(jobId);
        }

        private static string CreateTempDirectory(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static void CreateRustOcrPlaceholders(string appRoot)
        {
            string sidecarRoot = Path.Combine(appRoot, "sidecar", "ocr");
            Directory.CreateDirectory(sidecarRoot);
            File.WriteAllText(Path.Combine(sidecarRoot, OperatingSystem.IsWindows() ? "exportdoc-ocr.exe" : "exportdoc-ocr"), string.Empty);
            string modelBasePath = Path.Combine(appRoot, "OcrModels", "PaddleOCR", "V6");
            string detDir = Path.Combine(modelBasePath, "det");
            string recDir = Path.Combine(modelBasePath, "rec");
            Directory.CreateDirectory(detDir);
            Directory.CreateDirectory(recDir);
            File.WriteAllText(Path.Combine(detDir, "inference.onnx"), string.Empty);
            File.WriteAllText(Path.Combine(detDir, "inference.yml"), "model_name: test");
            File.WriteAllText(Path.Combine(recDir, "inference.onnx"), string.Empty);
            File.WriteAllText(Path.Combine(recDir, "inference.yml"), "character_dict:\n- A");
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private sealed class PatternInvoiceService : IInvoiceService
        {
            public Task<SaveResult> SaveInvoiceWithAutoCreationAsync(
                Invoice invoice,
                List<Item> items,
                Customer customer,
                Exporter exporter)
            {
                throw new NotSupportedException();
            }

            public Task<bool> SaveInvoiceAsync(Invoice invoice)
            {
                throw new NotSupportedException();
            }

            public Task<bool> DeleteInvoiceAsync(int id)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetInvoiceByIdAsync(int id)
            {
                return Task.FromResult(new Invoice
                {
                    Id = id,
                    InvoiceNo = "PATTERN-001",
                    CustomerId = 7,
                    CustomerNameEN = "Pattern Customer",
                    Type = "实际数据"
                });
            }

            public Task<Invoice> GetInvoiceByInvoiceNoAndTypeAsync(string invoiceNo, string type)
            {
                throw new NotSupportedException();
            }

            public Task<bool> InvoiceNoExistsAsync(string invoiceNo)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> CopyInvoiceAsync(int originalId, string newInvoiceNo, InvoiceCloneOptions options = null)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> CopyInvoiceAsTypeAsync(int originalId, string targetType, InvoiceCloneOptions options = null)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> UnverifyInvoiceAsync(int id)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetLatestInvoiceByPartiesAsync(int? customerId, int? exporterId)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetLastInvoiceAsync()
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestReportPdfRenderService : IReportPdfRenderService
        {
            public Task<ReportPdfRenderResult> RenderInvoicePdfAsync(
                ReportPdfRenderRequest request,
                CancellationToken cancellationToken = default)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
                File.WriteAllText(request.DestinationPath, "%PDF-1.4 test");
                return Task.FromResult(new ReportPdfRenderResult
                {
                    SourceId = request.SourceId,
                    ReportType = request.ReportType,
                    TemplatePath = request.TemplatePath,
                    WithSeal = request.WithSeal,
                    DestinationPath = request.DestinationPath,
                    RendererKind = "Test"
                });
            }

            public Task<ReportPdfRenderResult> RenderPaymentVoucherPdfAsync(
                ReportPdfRenderRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestSettingsService : ISettingsService
        {
            public TestSettingsService(AppSettings settings)
            {
                Settings = settings;
            }

            public AppSettings Settings { get; }

            public Task LoadAsync() => Task.CompletedTask;

            public Task SaveAsync() => Task.CompletedTask;
        }

        private sealed class TestPdfMergeService : IPdfMergeService
        {
            public void Merge(
                IReadOnlyCollection<string> sourceFiles,
                string destinationPath,
                CancellationToken cancellationToken = default)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.WriteAllText(destinationPath, string.Join(Environment.NewLine, sourceFiles));
            }
        }

        private sealed class StubUserService : IUserService
        {
            private readonly User _user;

            public StubUserService(User user)
            {
                _user = user;
            }

            public Task<User> AuthenticateAsync(string username, string password)
            {
                throw new NotSupportedException();
            }

            public Task<User> GetUserByUsernameAsync(string username)
            {
                return Task.FromResult(string.Equals(_user.Username, username, StringComparison.OrdinalIgnoreCase)
                    ? _user
                    : null);
            }

            public Task<User> GetActiveUserByIdAsync(int userId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_user.Id == userId && _user.IsActive ? _user : null);
            }

            public Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<int> SaveUserAsync(
                User user,
                string resetPassword = "",
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ThrowingInvoiceService : IInvoiceService
        {
            public Task<SaveResult> SaveInvoiceWithAutoCreationAsync(
                Invoice invoice,
                List<Item> items,
                Customer customer,
                Exporter exporter)
            {
                throw new NotSupportedException();
            }

            public Task<bool> SaveInvoiceAsync(Invoice invoice)
            {
                throw new NotSupportedException();
            }

            public Task<bool> DeleteInvoiceAsync(int id)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetInvoiceByIdAsync(int id)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetInvoiceByInvoiceNoAndTypeAsync(string invoiceNo, string type)
            {
                throw new NotSupportedException();
            }

            public Task<bool> InvoiceNoExistsAsync(string invoiceNo)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> CopyInvoiceAsync(int originalId, string newInvoiceNo, InvoiceCloneOptions options = null)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> CopyInvoiceAsTypeAsync(int originalId, string targetType, InvoiceCloneOptions options = null)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> UnverifyInvoiceAsync(int id)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetLatestInvoiceByPartiesAsync(int? customerId, int? exporterId)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice> GetLastInvoiceAsync()
            {
                throw new NotSupportedException();
            }
        }
    }
}

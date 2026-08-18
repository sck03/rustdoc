using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.BrowserRuntime;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Tools;
using ExportDocManager.Services.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
            string endpointFile = Path.Combine(dataRoot, "Cache", "Sidecar", "endpoint.json");

            var options = ApiRuntimeOptions.Parse(
            [
                "--app-root", appRoot,
                "--data-root", dataRoot,
                "--urls", "http://127.0.0.1:5199",
                "--endpoint-file", endpointFile,
                "--network-mode", "true",
                "--path-base", "/exportdoc/",
                "--business-time-zone", "Asia/Shanghai",
                "--allowed-origins", "https://erp.example.com;http://192.168.1.20:8080",
                "--trusted-proxies", "172.30.238.10;::1"
            ]);

            Assert.Equal(Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar), options.AppRoot);
            Assert.Equal(Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar), options.DataRoot);
            Assert.Equal("http://127.0.0.1:5199", options.ListenUrls);
            Assert.Equal(Path.GetFullPath(endpointFile), options.EndpointFile);
            Assert.True(options.NetworkMode);
            Assert.Equal("/exportdoc", options.PathBase);
            Assert.Equal("Asia/Shanghai", options.BusinessTimeZoneId);
            Assert.Equal(2, options.AllowedOrigins.Count);
            Assert.Equal(2, options.TrustedProxies.Count);
            Assert.Contains(IPAddress.Parse("172.30.238.10"), options.TrustedProxies);
            Assert.Contains(IPAddress.IPv6Loopback, options.TrustedProxies);
        }

        [Fact]
        public void SecurityHeaders_ShouldRestrictConnectionsToSelfAndConfiguredOrigins()
        {
            string policy = ApiSecurityHeadersMiddleware.BuildContentSecurityPolicy(new ApiRuntimeOptions
            {
                AllowedOrigins = ["https://erp.example.com", "http://192.168.1.20:8080"]
            });

            Assert.Contains("connect-src 'self' https://erp.example.com http://192.168.1.20:8080;", policy);
            Assert.DoesNotContain("connect-src 'self' http: https:", policy);
            Assert.DoesNotContain(" ws: ", policy);
        }

        [Fact]
        public void Parse_ShouldPreserveFileSystemRootPaths()
        {
            string root = Path.GetPathRoot(Path.GetFullPath(AppContext.BaseDirectory)) ??
                throw new InvalidOperationException("当前平台没有可用的文件系统根目录。");
            var options = ApiRuntimeOptions.Parse(["--app-root", root, "--data-root", root]);
            Assert.Equal(root, options.AppRoot);
            Assert.Equal(root, options.DataRoot);
        }

        [Fact]
        public void EndpointPublication_ShouldRequireProcessBoundDynamicLoopbackConfiguration()
        {
            string appRoot = CreateTempDirectory("edm-api-endpoint-app");
            string dataRoot = CreateTempDirectory("edm-api-endpoint-data");
            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                ApiStartupValidator.PrepareRuntimeDirectories(pathProvider);
                string endpointFile = Path.Combine(pathProvider.CacheRoot, "Sidecar", "endpoint.json");
                var valid = new ApiRuntimeOptions
                {
                    ListenUrls = "http://127.0.0.1:0",
                    DesktopAccessToken = "desktop-secret",
                    EndpointFile = endpointFile
                };

                ApiStartupValidator.ValidateEndpointPublication(pathProvider, valid);
                Assert.Equal(
                    "http://127.0.0.1:5199",
                    ApiEndpointPublication.ResolveApiBaseUrl(["http://127.0.0.1:5199"]));
                ApiEndpointPublication.Publish(endpointFile, ["http://127.0.0.1:5199"]);
                byte[] publicationBytes = File.ReadAllBytes(endpointFile);
                Assert.NotEmpty(publicationBytes);
                Assert.Equal((byte)'{', publicationBytes[0]);
                Assert.False(publicationBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
                using (JsonDocument publication = JsonDocument.Parse(publicationBytes))
                {
                    Assert.Equal(1, publication.RootElement.GetProperty("schemaVersion").GetInt32());
                    Assert.Equal(
                        "http://127.0.0.1:5199",
                        publication.RootElement.GetProperty("apiBaseUrl").GetString());
                    Assert.Equal(
                        Environment.ProcessId,
                        publication.RootElement.GetProperty("processId").GetInt32());
                }
                ApiEndpointPublication.Remove(endpointFile);
                Assert.False(File.Exists(endpointFile));
                ApiEndpointPublication.Publish(
                    endpointFile,
                    ["http://127.0.0.1:5199"],
                    "/exportdoc");
                using (JsonDocument publication = JsonDocument.Parse(File.ReadAllBytes(endpointFile)))
                {
                    Assert.Equal(
                        "http://127.0.0.1:5199/exportdoc",
                        publication.RootElement.GetProperty("apiBaseUrl").GetString());
                }
                ApiEndpointPublication.Remove(endpointFile);
                Assert.Throws<InvalidOperationException>(() => ApiEndpointPublication.ResolveApiBaseUrl(
                    ["http://127.0.0.1:5199", "http://127.0.0.1:5200"]));
                Assert.Throws<InvalidOperationException>(() => ApiStartupValidator.ValidateEndpointPublication(
                    pathProvider,
                    new ApiRuntimeOptions
                    {
                        ListenUrls = "http://127.0.0.1:5188",
                        DesktopAccessToken = valid.DesktopAccessToken,
                        EndpointFile = valid.EndpointFile
                    }));
                Assert.ThrowsAny<UnauthorizedAccessException>(() => ApiStartupValidator.ValidateEndpointPublication(
                    pathProvider,
                    new ApiRuntimeOptions
                    {
                        ListenUrls = valid.ListenUrls,
                        DesktopAccessToken = valid.DesktopAccessToken,
                        EndpointFile = Path.Combine(dataRoot, "endpoint.json")
                    }));
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Theory]
        [InlineData("exportdoc")]
        [InlineData("/exportdoc/../admin")]
        [InlineData("/exportdoc?debug=true")]
        [InlineData("/exportdoc%2Fadmin")]
        public void Parse_ShouldRejectUnsafePathBase(string value)
        {
            Assert.Throws<InvalidOperationException>(() =>
                ApiRuntimeOptions.Parse(["--path-base", value]));
        }

        [Fact]
        public void ForwardedHeaders_ShouldOnlyTrustConfiguredProxyAddresses()
        {
            var options = ApiForwardedHeaders.CreateOptions(new ApiRuntimeOptions
            {
                TrustedProxies = [IPAddress.Parse("172.30.238.10")]
            });

            Assert.Equal(
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
                options.ForwardedHeaders);
            Assert.Equal(1, options.ForwardLimit);
            Assert.Contains(IPAddress.Parse("172.30.238.10"), options.KnownProxies);
            Assert.DoesNotContain(IPAddress.Parse("10.0.0.5"), options.KnownProxies);
        }

        [Fact]
        public void Validate_ShouldCreateRuntimeDataDirectoriesForRelativeSqliteDatabase()
        {
            string appRoot = CreateTempDirectory("edm-api-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);

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
                string databasePath = DbHelper.GetDatabasePath(pathProvider, databaseSettings.SqliteDatabaseFileName);

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
        public void Validate_ShouldOpenSqliteDatabaseWithoutLeakingItsPathOnFailure()
        {
            string appRoot = CreateTempDirectory("edm-api-sqlite-probe-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-sqlite-probe-data-{Guid.NewGuid():N}");

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                ApiStartupValidator.PrepareRuntimeDirectories(pathProvider);
                string databasePath = Path.Combine(pathProvider.DatabaseRoot, "occupied.db");
                Directory.CreateDirectory(databasePath);

                var exception = Assert.Throws<InvalidOperationException>(() => ApiStartupValidator.Validate(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "occupied.db"
                    },
                    new ApiRuntimeOptions
                    {
                        AppRoot = appRoot,
                        DataRoot = dataRoot,
                        ListenUrls = "http://127.0.0.1:5199"
                    }));

                Assert.Contains("本地数据库无法打开", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(databasePath, exception.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(nameof(Microsoft.Data.Sqlite.SqliteException), exception.Message, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void Validate_ShouldAllowOnlySqliteDatabasePathsInsideRuntimeDatabaseRoot()
        {
            string appRoot = CreateTempDirectory("edm-api-path-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-api-path-data-{Guid.NewGuid():N}");
            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var runtimeOptions = new ApiRuntimeOptions
                {
                    AppRoot = appRoot,
                    DataRoot = dataRoot,
                    ListenUrls = "http://127.0.0.1:5199"
                };

                ApiStartupValidator.Validate(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = Path.Combine(pathProvider.DatabaseRoot, "inside.db")
                    },
                    runtimeOptions);

                Assert.Throws<ServiceValidationException>(() => ApiStartupValidator.Validate(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "..\\outside.db"
                    },
                    runtimeOptions));
                Assert.Throws<ServiceValidationException>(() => ApiStartupValidator.Validate(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "tenant/data.db"
                    },
                    runtimeOptions));
                Assert.Throws<ServiceValidationException>(() => ApiStartupValidator.Validate(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = "C:\\outside.db"
                    },
                    runtimeOptions));
                Assert.Throws<ServiceValidationException>(() => ApiStartupValidator.Validate(
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.SqliteProvider,
                        SqliteDatabaseFileName = Path.Combine(appRoot, "outside.db")
                    },
                    runtimeOptions));

                string browserDownloadRoot = Path.Combine(pathProvider.ExportRoot, "Browser");
                string externalDownloadRoot = Path.Combine(dataRoot, "ExternalBrowserTarget");
                Directory.Delete(browserDownloadRoot, recursive: true);
                Directory.CreateDirectory(externalDownloadRoot);
                try
                {
                    Directory.CreateSymbolicLink(browserDownloadRoot, externalDownloadRoot);
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    PlatformNotSupportedException)
                {
                    return;
                }

                try
                {
                    Assert.Throws<InvalidOperationException>(() => ApiStartupValidator.Validate(
                        pathProvider,
                        new DatabaseConnectionSettings
                        {
                            Provider = DatabaseConnectionSettings.SqliteProvider,
                            SqliteDatabaseFileName = Path.Combine(pathProvider.DatabaseRoot, "inside.db")
                        },
                        runtimeOptions));
                    Assert.Empty(Directory.EnumerateFileSystemEntries(externalDownloadRoot));
                }
                finally
                {
                    Directory.Delete(browserDownloadRoot);
                }
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public void PrepareRuntimeDirectories_WhenAncestorIsLink_ShouldNotWriteOutsideBoundary()
        {
            string appRoot = CreateTempDirectory("edm-api-link-app");
            string root = CreateTempDirectory("edm-api-link-root");
            string externalRoot = CreateTempDirectory("edm-api-link-external");
            string linkRoot = Path.Combine(root, "linked-data");
            string dataRoot = Path.Combine(linkRoot, "business-data");

            try
            {
                try
                {
                    Directory.CreateSymbolicLink(linkRoot, externalRoot);
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    PlatformNotSupportedException)
                {
                    return;
                }

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);

                Assert.Throws<InvalidOperationException>(
                    () => ApiStartupValidator.PrepareRuntimeDirectories(pathProvider));
                Assert.Empty(Directory.EnumerateFileSystemEntries(externalRoot));
            }
            finally
            {
                if (Directory.Exists(linkRoot)) Directory.Delete(linkRoot);
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(root);
                DeleteDirectoryIfExists(externalRoot);
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
        public void ValidateBootstrapToken_ShouldProtectFirstNetworkAdministratorInitialization()
        {
            var postgreSql = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "postgres",
                PostgreSqlDatabase = "exportdoc",
                PostgreSqlUsername = "exportdoc"
            };

            Assert.Throws<InvalidOperationException>(() => ApiStartupValidator.ValidateBootstrapToken(
                new ApiRuntimeOptions { NetworkMode = true },
                postgreSql));

            ApiStartupValidator.ValidateBootstrapToken(
                new ApiRuntimeOptions { NetworkMode = true, BootstrapToken = new string('x', 24) },
                postgreSql);
            ApiStartupValidator.ValidateBootstrapToken(
                new ApiRuntimeOptions { NetworkMode = false },
                postgreSql);
        }

        [Fact]
        public void Documentation_ShouldRequireAuthenticationOnlyInNetworkMode()
        {
            Assert.False(ApiEndpointAuth.RequiresDocumentationAuthentication(new ApiRuntimeOptions()));
            Assert.True(ApiEndpointAuth.RequiresDocumentationAuthentication(new ApiRuntimeOptions { NetworkMode = true }));
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

        [Theory]
        [InlineData("/readyz")]
        [InlineData("/livez")]
        [InlineData("/healthz")]
        public async Task CorsPolicy_ShouldExposePublicProbesToTauriLocalhost(string path)
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-probe-cors",
                "probe-cors-test.db",
                desktopAccessToken: "desktop-secret");
            using var client = harness.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Origin", "http://tauri.localhost");

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
            Assert.Contains("http://tauri.localhost", origins);
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
        public void DocumentPermissionTemplate_ShouldExposeHsReadOnlyWithoutMasterDataMaintenance()
        {
            var service = new ApiAuthorizationService(new ApiRuntimeOptions
            {
                ProductEdition = ProductEditionCatalog.Full
            });

            var capabilities = service.GetCapabilities(new User { Role = UserRoleCatalog.User });
            var hsGrant = Assert.Single(capabilities.ModuleAccess, grant =>
                grant.ModuleKey == PermissionModuleCatalog.DocumentHsKnowledge);

            Assert.Equal(PermissionAccessLevel.View, hsGrant.AccessLevel);
            Assert.Contains(PermissionModuleCatalog.DocumentMasterData, capabilities.EnabledModules);
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

            var dto = ApiUserDtoFactory.FromUser(
                user,
                service,
                new BusinessClock(
                    new FixedTimeProvider(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero)),
                    BusinessClock.DefaultTimeZoneId));

            Assert.Equal(7, dto.Id);
            Assert.True(dto.Capabilities.CanManageSettings);
            Assert.True(dto.Capabilities.CanManageUsers);
            Assert.True(dto.Capabilities.CanViewAllBusinessData);
            Assert.Equal(new DateOnly(2026, 8, 17), dto.BusinessDate);
            Assert.Equal(BusinessClock.DefaultTimeZoneId, dto.BusinessTimeZone);

            var utcDto = ApiUserDtoFactory.FromUser(
                user,
                service,
                new BusinessClock(
                    new FixedTimeProvider(new DateTimeOffset(2026, 8, 17, 23, 0, 0, TimeSpan.Zero)),
                    "UTC"));
            Assert.Equal(new DateOnly(2026, 8, 17), utcDto.BusinessDate);
            Assert.Equal("UTC", utcDto.BusinessTimeZone);
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
        [InlineData("GET", PermissionModuleCatalog.DocumentReferenceData, PermissionAccessLevel.View)]
        [InlineData("POST", PermissionModuleCatalog.DocumentMasterData, PermissionAccessLevel.Operate)]
        [InlineData("DELETE", PermissionModuleCatalog.DocumentMasterData, PermissionAccessLevel.Manage)]
        public void EndpointPermissionMetadata_ShouldResolveMethodPolicy(
            string method,
            string expectedModule,
            string expectedAccess)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            var metadata = new ApiEndpointPermissionMetadata(
                PermissionModuleCatalog.DocumentReferenceData,
                PermissionModuleCatalog.DocumentMasterData);

            var requirement = metadata.Resolve(context);

            Assert.Equal(expectedModule, requirement.Module);
            Assert.Equal(expectedAccess, requirement.AccessLevel);
        }

        [Theory]
        [InlineData("ExportDocument", PermissionModuleCatalog.DocumentInvoiceReports)]
        [InlineData("PaymentVoucher", PermissionModuleCatalog.DocumentPaymentReports)]
        [InlineData("Unknown", PermissionModuleCatalog.DocumentReports)]
        public void EndpointPermissionMetadata_ShouldResolveReportType(
            string reportType,
            string expectedModule)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            context.Request.QueryString = new QueryString($"?reportType={reportType}");
            var metadata = new ApiEndpointPermissionMetadata(
                PermissionModuleCatalog.DocumentReports,
                Selector: ApiPermissionSelector.ReportType);

            Assert.Equal(expectedModule, metadata.Resolve(context).Module);
        }

        [Fact]
        public async Task UnknownBusinessApiRoute_ShouldRemainNotFound()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-unknown-route",
                "unknown-route.db");
            using var response = await harness.CreateClient().GetAsync("/api/invoices/not-exist");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        public void InvoiceDocumentEmailDefaults_ShouldApplyConfiguredPlaceholders()
        {
            var documentSet = new ApiEndpointRouteBuilderExtensions.ApiInvoiceGeneratedDocumentSet(
                "INV-2026-01",
                "Acme Buyer",
                12,
                new DateOnly(2026, 6, 24),
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
                services.AddSingleton<IBusinessClock>(BusinessClock.CreateSystem());
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
                    new DateOnly(2026, 6, 24),
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
                await settingsService.UpdateAsync(settings =>
                {
                    settings.System.DefaultTemplateExporterNameCn = "宁波测试出口有限公司";
                    return true;
                });
                var excelImportService = scope.ServiceProvider.GetRequiredService<IExcelImportService>();

                var result = await excelImportService.ImportFromExcelAsync(sourcePath);
                var response = ApiExcelDtoFactory.FromImportResult(Path.GetFullPath(sourcePath), result);

                Assert.True(response.Success);
                Assert.Equal(Path.GetFullPath(sourcePath), response.SourcePath);
                var invoice = Assert.IsType<ApiInvoiceDetailDto>(response.Invoice);
                var customer = Assert.IsType<ApiImportedCustomerDto>(response.Customer);
                var exporter = Assert.IsType<ApiImportedExporterDto>(response.Exporter);
                Assert.Equal("INV-XLS-001", invoice.InvoiceNo);
                Assert.Equal("Buyer Ltd", customer.CustomerNameEN);
                Assert.Equal("Exporter Ltd", exporter.ExporterNameEN);
                Assert.Equal("宁波测试出口有限公司", exporter.ExporterNameCN);
                Assert.Single(invoice.Items);
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
                Status = BackgroundJobStatusCatalog.Running,
                CreatedAt = DateTimeOffset.UtcNow
            });
            service.Upsert(new BackgroundJobSnapshot
            {
                JobId = "succeeded",
                Title = "成功",
                Status = BackgroundJobStatusCatalog.Running,
                CreatedAt = DateTimeOffset.UtcNow
            });
            service.Update("failed", current => CopyJobState(current, current.Status, 10));
            service.Update("succeeded", current => CopyJobState(current, current.Status, 20));
            service.Update("failed", current => CopyJobState(current, BackgroundJobStatusCatalog.Failed, current.ProgressPercent));
            service.Update("succeeded", current => CopyJobState(current, BackgroundJobStatusCatalog.Succeeded, current.ProgressPercent));

            Assert.Equal(2, service.PersistThrottleEntryCount);

            bool deleteRunning = await service.DeleteAsync("running");
            bool deleteFailed = await service.DeleteAsync("failed");
            int cleared = await service.ClearTerminalAsync();

            Assert.False(deleteRunning);
            Assert.True(deleteFailed);
            Assert.Equal(1, cleared);
            Assert.NotNull(await service.GetAsync("running"));
            Assert.Null(await service.GetAsync("failed"));
            Assert.Null(await service.GetAsync("succeeded"));
            Assert.Equal(0, service.PersistThrottleEntryCount);
        }

        private static BackgroundJobSnapshot CopyJobState(
            BackgroundJobSnapshot current,
            string status,
            int? progressPercent)
        {
            return new BackgroundJobSnapshot
            {
                JobId = current.JobId,
                Kind = current.Kind,
                Title = current.Title,
                Status = status,
                ProgressPercent = progressPercent,
                StatusText = current.StatusText,
                DetailText = current.DetailText,
                RequestedBy = current.RequestedBy,
                RequestedByUserId = current.RequestedByUserId,
                CreatedAt = current.CreatedAt,
                StartedAt = current.StartedAt,
                CompletedAt = current.CompletedAt,
                OutputPath = current.OutputPath,
                ErrorMessage = current.ErrorMessage,
                CanCancel = current.CanCancel,
                CanRetry = current.CanRetry,
                RetryOperation = current.RetryOperation,
                RetryRequestJson = current.RetryRequestJson
            };
        }

        [Fact]
        public async Task ApiBackgroundJobExecutionContext_ShouldIgnoreLateProgressAfterTerminalState()
        {
            var service = new ApiBackgroundJobService();
            var completedAt = DateTimeOffset.UtcNow;
            var terminal = service.Upsert(new BackgroundJobSnapshot
            {
                JobId = "late-progress",
                Kind = "QueryInvoiceExcelExport",
                Title = "导出查询结果 Excel",
                Status = BackgroundJobStatusCatalog.Succeeded,
                ProgressPercent = 100,
                StatusText = "已完成",
                DetailText = "已导出 1 条记录。",
                RequestedBy = "admin",
                RequestedByUserId = 1,
                CreatedAt = completedAt.AddSeconds(-1),
                StartedAt = completedAt.AddMilliseconds(-500),
                CompletedAt = completedAt,
                OutputPath = "query.xlsx",
                CanCancel = false,
                CanRetry = false
            });
            var context = new ApiBackgroundJobExecutionContext(service, terminal, CancellationToken.None);

            context.Report(75, "导出完成", "已写入 1 行。", "late.xlsx");

            var current = await service.GetAsync(terminal.JobId);
            Assert.NotNull(current);
            Assert.Equal(BackgroundJobStatusCatalog.Succeeded, current.Status);
            Assert.Equal(100, current.ProgressPercent);
            Assert.Equal("已完成", current.StatusText);
            Assert.Equal(completedAt, current.CompletedAt);
            Assert.Equal("query.xlsx", current.OutputPath);
            Assert.Equal(1, current.RequestedByUserId);
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
        public async Task ApiBackgroundJobService_CancelShouldRemainAcceptedWhenWorkerCompletesSynchronously()
        {
            var service = new ApiBackgroundJobService();
            const string jobId = "cancel-race";
            service.Upsert(new BackgroundJobSnapshot
            {
                JobId = jobId,
                Kind = "Test",
                Title = "取消竞态",
                Status = BackgroundJobStatusCatalog.Running,
                StatusText = "运行中",
                CreatedAt = DateTimeOffset.UtcNow,
                CanCancel = true
            });

            using var source = new CancellationTokenSource();
            service.RegisterCancellationSource(jobId, source);
            using var registration = source.Token.Register(() =>
                service.Update(jobId, current => new BackgroundJobSnapshot
                {
                    JobId = current.JobId,
                    Kind = current.Kind,
                    Title = current.Title,
                    Status = BackgroundJobStatusCatalog.Canceled,
                    StatusText = "已取消",
                    RequestedBy = current.RequestedBy,
                    CreatedAt = current.CreatedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    CanCancel = false
                }));

            bool accepted = await service.RequestCancelAsync(jobId);
            var final = await service.GetAsync(jobId);

            Assert.True(accepted);
            Assert.NotNull(final);
            Assert.Equal(BackgroundJobStatusCatalog.Canceled, final.Status);
            Assert.False(final.CanCancel);
        }

        [Fact]
        public async Task ApiBackgroundJobRunner_ShouldDeleteControlledPartialOutputWhenJobFails()
        {
            string appRoot = CreateTempDirectory("edm-job-cleanup-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-job-cleanup-data-{Guid.NewGuid():N}");
            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var jobService = new ApiBackgroundJobService(pathProvider);
                var services = new ServiceCollection();
                using var provider = services.BuildServiceProvider();
                var runner = new ApiBackgroundJobRunner(
                    jobService,
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<ApiBackgroundJobRunner>.Instance);
                string outputDirectory = Path.Combine(pathProvider.ExportRoot, "Browser", "Test", Guid.NewGuid().ToString("N"));
                string outputPath = Path.Combine(outputDirectory, "partial.pdf");

                var job = runner.Enqueue("Test", "失败清理", "admin", (_, context) =>
                {
                    Directory.CreateDirectory(outputDirectory);
                    File.WriteAllText(outputPath, "partial");
                    context.Report(30, "生成中");
                    throw new InvalidOperationException("expected failure");
                }, initialOutputPath: outputPath);

                var final = await WaitForJobStatusAsync(jobService, job.JobId, BackgroundJobStatusCatalog.Failed);
                await runner.StopAsync(CancellationToken.None);
                Assert.Equal(string.Empty, final.OutputPath);
                Assert.False(File.Exists(outputPath));
                Assert.False(Directory.Exists(outputDirectory));
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        [Fact]
        public async Task ApiBackgroundJobRunner_ShouldDeleteProducedOutputWhenCancellationWinsCompletionRace()
        {
            string appRoot = CreateTempDirectory("edm-job-cancel-output-app");
            string dataRoot = Path.Combine(Path.GetTempPath(), $"edm-job-cancel-output-data-{Guid.NewGuid():N}");
            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var jobService = new ApiBackgroundJobService(pathProvider);
                var services = new ServiceCollection();
                using var provider = services.BuildServiceProvider();
                var runner = new ApiBackgroundJobRunner(
                    jobService,
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<ApiBackgroundJobRunner>.Instance);
                string outputDirectory = Path.Combine(pathProvider.ExportRoot, "Browser", "Test", Guid.NewGuid().ToString("N"));
                string outputPath = Path.Combine(outputDirectory, "completed-before-cancel.pdf");

                var job = runner.Enqueue("Test", "完成竞态清理", "admin", async (_, context) =>
                {
                    Directory.CreateDirectory(outputDirectory);
                    await File.WriteAllTextAsync(outputPath, "completed", context.CancellationToken);
                    Assert.True(await jobService.RequestCancelAsync(context.JobId));
                    return outputPath;
                });

                var final = await WaitForJobStatusAsync(jobService, job.JobId, BackgroundJobStatusCatalog.Canceled);
                await runner.StopAsync(CancellationToken.None);
                Assert.Equal(string.Empty, final.OutputPath);
                Assert.False(File.Exists(outputPath));
                Assert.False(Directory.Exists(outputDirectory));
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
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
            services.AddSingleton<ApiBackgroundJobExecutionUserAccessor>();
            services.AddSingleton<ApiCurrentUserResolver>();
            services.AddSingleton<IApiSessionTokenService, InMemoryApiSessionTokenService>();
            services.AddSingleton<ICurrentUserContext, ApiCurrentUserContext>();
            using var provider = services.BuildServiceProvider();
            var runner = new ApiBackgroundJobRunner(
                jobService,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ApiBackgroundJobRunner>.Instance);
            var observedUser = new TaskCompletionSource<User?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var job = runner.Enqueue(
                "Test",
                "用户上下文任务",
                "operator-job",
                (scope, _) =>
                {
                    observedUser.SetResult(scope.GetRequiredService<ICurrentUserContext>().CurrentUser);
                    return Task.FromResult(string.Empty);
                });

            var user = Assert.IsType<User>(await observedUser.Task.WaitAsync(TimeSpan.FromSeconds(5)));
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
            string secondSourcePath = Path.Combine(tempRoot, "source-2.pdf");
            string destinationPath = Path.Combine(tempRoot, "merged.pdf");

            try
            {
                await File.WriteAllTextAsync(sourcePath, "%PDF-1.4");
                await File.WriteAllTextAsync(secondSourcePath, "%PDF-1.4");
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
                            SourceFiles = new List<string> { sourcePath, secondSourcePath },
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
            var response = ApiSingleWindowDtoFactory.FromClientProfiles([
                new SwClientProfile
                {
                    Id = 3,
                    ProfileKey = "SWP-11111111111111111111111111111111",
                    ProfileName = "Profile A",
                    CustomsCooClientRootPath = "D:\\SingleWindow\\Coo",
                    AgentConsignmentClientRootPath = "D:\\SingleWindow\\Acd",
                    CanSubmitAgentConsignment = true,
                    CanSubmitCustomsCoo = false,
                    IsEnabled = true,
                    IsActive = true,
                    UpdatedAt = new DateTime(2026, 6, 23)
                }
            ]);

            Assert.Single(response.Profiles);
            Assert.Equal(3, response.Profiles[0].Id);
            Assert.Equal("Profile A", response.Profiles[0].ProfileName);
            Assert.Equal("D:\\SingleWindow\\Acd", response.Profiles[0].AgentConsignmentClientRootPath);
            Assert.False(response.Profiles[0].CanSubmitCustomsCoo);
            Assert.Equal("SWP-11111111111111111111111111111111", response.ActiveProfileKey);
            Assert.Contains("SQLite", response.StoragePolicy, StringComparison.Ordinal);
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
                Assert.IsType<SingleWindowClientProfileService>(profilePort);
                Assert.IsType<ManualImportClientBridge>(bridgePort);
                Assert.NotSame(profilePort, bridgePort);
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
                string? previousRuntime = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");

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
                string? previousRuntime = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");

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
                string? previousRuntime = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_OCR_RUNTIME");

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
                ApiStartupValidator.PrepareRuntimeDirectories(pathProvider);
                var response = ApiHealthResponseFactory.Create(
                    pathProvider,
                    new DatabaseConnectionSettings(),
                    Path.Combine(pathProvider.DatabaseRoot, "exportdoc.db"),
                    new RuntimeDependencyDiagnosticsService(
                        pathProvider,
                        [
                            new BrowserRuntimeDiagnosticContributor(
                                pathProvider,
                                new BrowserExecutableResolver(pathProvider)),
                            new OcrRuntimeDiagnosticContributor(pathProvider)
                        ]).Inspect());
                var publicResponse = ApiHealthResponseFactory.CreatePublic(new DatabaseConnectionSettings());

                Assert.Equal("ok", response.Status);
                Assert.False(string.IsNullOrWhiteSpace(response.ProductVersion));
                Assert.False(string.IsNullOrWhiteSpace(response.InformationalVersion));
                Assert.Equal(appRoot, response.AppRoot);
                Assert.Equal(dataRoot, response.DataRoot);
                Assert.Contains(response.RuntimePaths, item =>
                    item.Key == "template-root" &&
                    item.StorageClass == "program-resource" &&
                    item.AccessMode == "read-only" &&
                    item.Requirement == ApiRuntimePathRequirement.Feature &&
                    item.Path == Path.Combine(appRoot, "Templates") &&
                    !item.Exists);
                Assert.Contains(response.RuntimePaths, item =>
                    item.Key == "user-template-root" &&
                    item.StorageClass == "runtime-data" &&
                    item.AccessMode == "read-write" &&
                    item.Requirement == ApiRuntimePathRequirement.Feature &&
                    item.Path == Path.Combine(dataRoot, "Templates") &&
                    item.Exists);
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
                var reportRenderer = Assert.Single(
                    response.RuntimeDependencies,
                    item => item.Key == "report-renderer");
                Assert.Equal(ApiRuntimePathRequirement.Feature, reportRenderer.Requirement);
                string? configuredRenderer = Environment.GetEnvironmentVariable(
                    BrowserExecutableResolver.ChromiumExecutableEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(configuredRenderer))
                {
                    Assert.Equal("missing", reportRenderer.Status);
                    Assert.False(reportRenderer.Ready);
                }
                else
                {
                    Assert.Equal("ready", reportRenderer.Status);
                    Assert.True(reportRenderer.Ready);
                    Assert.Equal(
                        Path.GetFullPath(configuredRenderer.Trim().Trim('"')),
                        reportRenderer.ResolvedPath,
                        ignoreCase: OperatingSystem.IsWindows());
                }
                Assert.Equal(string.Empty, publicResponse.AppRoot);
                Assert.Equal(string.Empty, publicResponse.DataRoot);
                Assert.Empty(publicResponse.RuntimePaths);
                Assert.Empty(publicResponse.RuntimeDependencies);
                Assert.Contains("公开健康检查", publicResponse.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains(response.RuntimeDependencies, item =>
                    item.Key == "postgresql-tools" &&
                    item.Requirement == ApiRuntimePathRequirement.Optional &&
                    !item.Ready);
                Assert.Contains("Templates/Resources/Browsers/Tools/OcrModels", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("设置、用户模板、数据库、日志", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("App_Data", response.StoragePolicy, StringComparison.Ordinal);
                Assert.Contains("--data-root", response.StoragePolicy, StringComparison.Ordinal);
            }
            finally
            {
                DeleteDirectoryIfExists(appRoot);
                DeleteDirectoryIfExists(dataRoot);
            }
        }

        private static (int StatusCode, object? Value) ReadResult(IResult result)
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

            return await service.GetAsync(jobId)
                ?? throw new TimeoutException($"Background job {jobId} did not reach {expectedStatus}.");
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
                List<Item>? items,
                Customer? customer,
                Exporter? exporter,
                IReadOnlyList<HsCodeKnowledgeFeedbackInput>? pendingHsFeedback = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> SaveInvoiceAsync(Invoice invoice, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> DeleteInvoiceAsync(int id, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetInvoiceByIdAsync(int id, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<Invoice?>(new Invoice
                {
                    Id = id,
                    InvoiceNo = "PATTERN-001",
                    CustomerId = 7,
                    CustomerNameEN = "Pattern Customer",
                    Type = "实际数据"
                });
            }

            public Task<Invoice?> GetInvoiceByInvoiceNoAndTypeAsync(
                string companyScope,
                string invoiceNo,
                string type,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> InvoiceNoExistsAsync(
                string companyScope,
                string invoiceNo,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> CopyInvoiceAsync(
                int originalId,
                string newInvoiceNo,
                InvoiceCloneOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> CopyInvoiceAsTypeAsync(
                int originalId,
                string targetType,
                InvoiceCloneOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> TransitionInvoiceStatusAsync(
                InvoiceStatusTransitionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> UnverifyInvoiceAsync(
                int id,
                byte[] expectedRowVersion,
                string note,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<InvoiceStatusHistory>> ListInvoiceStatusHistoryAsync(
                int invoiceId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetLatestInvoiceByPartiesAsync(
                int? customerId,
                int? exporterId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetLastInvoiceAsync(CancellationToken cancellationToken = default)
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
                PaymentReportPdfRenderRequest request,
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

            public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<bool> UpdateAsync(
                Func<AppSettings, bool> update,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(update(Settings));
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

            public Task<User?> AuthenticateAsync(string username, string password)
            {
                throw new NotSupportedException();
            }

            public Task<User?> GetUserByUsernameAsync(string username)
            {
                return Task.FromResult<User?>(string.Equals(_user.Username, username, StringComparison.OrdinalIgnoreCase)
                    ? _user
                    : null);
            }

            public Task<User?> GetActiveUserByIdAsync(int userId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<User?>(_user.Id == userId && _user.IsActive ? _user : null);
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
                List<Item>? items,
                Customer? customer,
                Exporter? exporter,
                IReadOnlyList<HsCodeKnowledgeFeedbackInput>? pendingHsFeedback = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> SaveInvoiceAsync(Invoice invoice, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> DeleteInvoiceAsync(int id, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetInvoiceByIdAsync(int id, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetInvoiceByInvoiceNoAndTypeAsync(
                string companyScope,
                string invoiceNo,
                string type,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<bool> InvoiceNoExistsAsync(
                string companyScope,
                string invoiceNo,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> CopyInvoiceAsync(
                int originalId,
                string newInvoiceNo,
                InvoiceCloneOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> CopyInvoiceAsTypeAsync(
                int originalId,
                string targetType,
                InvoiceCloneOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> TransitionInvoiceStatusAsync(
                InvoiceStatusTransitionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> UnverifyInvoiceAsync(
                int id,
                byte[] expectedRowVersion,
                string note,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<InvoiceStatusHistory>> ListInvoiceStatusHistoryAsync(
                int invoiceId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetLatestInvoiceByPartiesAsync(
                int? customerId,
                int? exporterId,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<Invoice?> GetLastInvoiceAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => utcNow;
        }
    }
}

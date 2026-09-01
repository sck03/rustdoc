using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Api.Tests
{
    internal sealed class ApiIntegrationTestHarness : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private readonly WebApplication _app;
        private readonly string _baseUrl;
        private bool _disposed;

        private ApiIntegrationTestHarness(
            WebApplication app,
            string appRoot,
            string dataRoot,
            string databasePath,
            string baseUrl)
        {
            _app = app;
            AppRoot = appRoot;
            DataRoot = dataRoot;
            DatabasePath = databasePath;
            _baseUrl = baseUrl;
        }

        public string AppRoot { get; }

        public string DataRoot { get; }

        public string DatabasePath { get; }

        public static async Task<ApiIntegrationTestHarness> StartAsync(
            string prefix,
            string databaseFileName,
            string? desktopAccessToken = null,
            string? productEdition = null,
            ILicenseSignatureVerifier? licenseSignatureVerifier = null,
            Action<IServiceCollection>? configureServices = null,
            string? pathBase = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseFileName);

            string appRoot = CreateTempDirectory($"{prefix}-app");
            string dataRoot = CreateTempDirectory($"{prefix}-data");
            return await StartCoreAsync(
                appRoot,
                dataRoot,
                databaseFileName,
                desktopAccessToken,
                productEdition,
                licenseSignatureVerifier,
                configureServices,
                pathBase,
                cleanupOnFailure: true);
        }

        public static async Task<ApiIntegrationTestHarness> StartWithExistingRootsAsync(
            string appRoot,
            string dataRoot,
            string databaseFileName,
            string? desktopAccessToken = null,
            string? productEdition = null,
            ILicenseSignatureVerifier? licenseSignatureVerifier = null,
            Action<IServiceCollection>? configureServices = null,
            string? pathBase = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseFileName);

            return await StartCoreAsync(
                appRoot,
                dataRoot,
                databaseFileName,
                desktopAccessToken,
                productEdition,
                licenseSignatureVerifier,
                configureServices,
                pathBase,
                cleanupOnFailure: false);
        }

        private static async Task<ApiIntegrationTestHarness> StartCoreAsync(
            string appRoot,
            string dataRoot,
            string databaseFileName,
            string? desktopAccessToken,
            string? productEdition,
            ILicenseSignatureVerifier? licenseSignatureVerifier,
            Action<IServiceCollection>? configureServices,
            string? pathBase,
            bool cleanupOnFailure)
        {
            string databasePath = Path.Combine(dataRoot, "Database", databaseFileName);
            const string listenUrl = "http://127.0.0.1:0";
            WebApplication? app = null;

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var databaseSettings = new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.SqliteProvider,
                    SqliteDatabaseFileName = databaseFileName
                };
                var runtimeOptions = new ApiRuntimeOptions
                {
                    AppRoot = appRoot,
                    DataRoot = dataRoot,
                    ListenUrls = listenUrl,
                    DesktopAccessToken = desktopAccessToken ?? string.Empty,
                    ProductEdition = ProductEditionCatalog.Normalize(productEdition ?? string.Empty),
                    PathBase = pathBase ?? string.Empty
                };

                ApiStartupValidator.Validate(pathProvider, databaseSettings, runtimeOptions);

                var builder = WebApplication.CreateBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.UseUrls(listenUrl);
                builder.Services.AddSingleton<IRuntimeLicenseAnchorStore>(
                    new FileRuntimeLicenseAnchorStore(
                        Path.Combine(dataRoot, "Security", "test-license-anchor.dat"),
                        "API 集成测试隔离授权锚点。"));
                if (licenseSignatureVerifier != null)
                {
                    builder.Services.AddSingleton(licenseSignatureVerifier);
                }
                builder.Services.AddExportDocManagerApiServices(pathProvider, databaseSettings, runtimeOptions);
                // Integration tests exercise endpoint behavior with a deliberately
                // isolated SQLite harness. Keep the real application's bounded
                // dependency probe covered by its focused tests while making the
                // generic HTTP harness deterministic and independent of startup
                // directory creation order.
                builder.Services.AddSingleton<IApiReadinessProbe>(new TestReadinessProbe());
                configureServices?.Invoke(builder.Services);

                app = builder.Build();
                app.UseExportDocManagerApiSafety();
                app.UseCors(ApiCorsPolicy.LocalFrontendPolicyName);
                app.UseExportDocManagerReadiness(databaseSettings, runtimeOptions);
                app.UsePathBase(runtimeOptions.PathBase);
                app.UseRouting();
                app.UseExportDocManagerResourceGovernance();
                app.UseExportDocManagerDesktopAccess();
                app.UseExportDocManagerApiAuthentication();
                app.UseExportDocManagerWorkspaceAccess();
                app.UseExportDocManagerLicenseRequirement();
                app.UseExportDocManagerSecurityAudit();
                app.MapExportDocManagerApiEndpoints(runtimeOptions, databaseSettings);
                await app.StartAsync();

                string baseUrl = ResolveBaseUrl(app);
                return new ApiIntegrationTestHarness(app, appRoot, dataRoot, databasePath, baseUrl);
            }
            catch
            {
                if (app != null)
                {
                    try
                    {
                        await app.StopAsync();
                    }
                    catch
                    {
                        // Startup may have failed before every hosted service was ready.
                    }

                    try
                    {
                        await app.DisposeAsync();
                    }
                    catch
                    {
                        // Preserve the original startup exception below.
                    }
                }

                if (cleanupOnFailure)
                {
                    ClearSqlitePool(databasePath);
                    DeleteDirectoryIfExists(appRoot);
                    DeleteDirectoryIfExists(dataRoot);
                }

                throw;
            }
        }

        public HttpClient CreateClient(string? accessToken = null, string? desktopAccessToken = null)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            if (!string.IsNullOrWhiteSpace(desktopAccessToken))
            {
                client.DefaultRequestHeaders.Add(ApiDesktopAccessOptions.HeaderName, desktopAccessToken);
            }

            return client;
        }

        public async Task<ApiLoginResponse> LoginAsync(
            HttpClient client,
            string username,
            string password)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username,
                password
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ReadJsonAsync<ApiLoginResponse>(response);
        }

        public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
        {
            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"无法解析 API 响应: {json}");
        }

        internal static string ResolveBaseUrl(WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses ?? app.Urls;
            return ApiEndpointPublication.ResolveApiBaseUrl(addresses);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAppAsync();
            ClearSqlitePool(DatabasePath);
            DeleteDirectoryIfExists(AppRoot);
            DeleteDirectoryIfExists(DataRoot);
        }

        public async ValueTask StopAppAsync()
        {
            if (_disposed)
            {
                return;
            }

            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _app.StopAsync(stopTimeout.Token);
            await _app.DisposeAsync();
            _disposed = true;
        }

        private static string CreateTempDirectory(string prefix)
        {
            string path = Path.Combine(GetTestRoot(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string GetTestRoot()
        {
            string configuredRoot = Environment.GetEnvironmentVariable("EXPORTDOCMANAGER_TEST_ROOT") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                string resolved = Path.GetFullPath(configuredRoot);
                Directory.CreateDirectory(resolved);
                return resolved;
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
                {
                    string workspaceRoot = Path.Combine(directory.FullName, ".codex-runtime", "api-tests");
                    Directory.CreateDirectory(workspaceRoot);
                    return workspaceRoot;
                }

                directory = directory.Parent;
            }

            string localRoot = Path.Combine(AppContext.BaseDirectory, ".test-runs");
            Directory.CreateDirectory(localRoot);
            return localRoot;
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    ClearSqlitePoolsForPath(path);
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    ClearSqlitePoolsForPath(path);
                    Thread.Sleep(100);
                }
            }
        }

        private static void ClearSqlitePool(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                return;
            }

            ClearSqlitePoolsForPath(databasePath);
            DeleteFileIfExists(databasePath + "-wal");
            DeleteFileIfExists(databasePath + "-shm");
        }

        private static void ClearSqlitePoolsForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                using var connection = new SqliteConnection(DbHelper.BuildConnectionString(path));
                SqliteConnection.ClearPool(connection);
                SqliteConnection.ClearAllPools();
            }
            catch (InvalidOperationException)
            {
                // The connection may already have been disposed during host shutdown.
            }
        }

        private static void DeleteFileIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private sealed class TestReadinessProbe : IApiReadinessProbe
        {
            public Task<ApiReadinessSnapshot> CheckAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new ApiReadinessSnapshot(
                    true,
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["database"] = "ready",
                        ["runtimeDirectories"] = "ready",
                        ["browser"] = "ready"
                    }));
        }
    }

    internal sealed class ApiTestLicenseSignatureVerifier : ILicenseSignatureVerifier
    {
        public const string ValidLicenseKey = "EDM2-API-TEST-LICENSE";
        public static readonly ApiTestLicenseSignatureVerifier Instance = new();

        public bool TryValidate(string machineId, string licenseKey, out DateOnly expireDate)
        {
            expireDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
            return !string.IsNullOrWhiteSpace(machineId) &&
                string.Equals(licenseKey, ValidLicenseKey, StringComparison.Ordinal);
        }
    }
}

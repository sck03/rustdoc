using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests
{
    internal sealed class ApiIntegrationTestHarness : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            string desktopAccessToken = null,
            string productEdition = null,
            ILicenseSignatureVerifier licenseSignatureVerifier = null)
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
                cleanupOnFailure: true);
        }

        public static async Task<ApiIntegrationTestHarness> StartWithExistingRootsAsync(
            string appRoot,
            string dataRoot,
            string databaseFileName,
            string desktopAccessToken = null,
            string productEdition = null,
            ILicenseSignatureVerifier licenseSignatureVerifier = null)
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
                cleanupOnFailure: false);
        }

        private static async Task<ApiIntegrationTestHarness> StartCoreAsync(
            string appRoot,
            string dataRoot,
            string databaseFileName,
            string desktopAccessToken,
            string productEdition,
            ILicenseSignatureVerifier licenseSignatureVerifier,
            bool cleanupOnFailure)
        {
            string databasePath = Path.Combine(dataRoot, "Database", databaseFileName);
            string baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";

            try
            {
                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var databaseSettings = new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.SqliteProvider,
                    SqliteDatabaseFileName = databasePath
                };
                var runtimeOptions = new ApiRuntimeOptions
                {
                    AppRoot = appRoot,
                    DataRoot = dataRoot,
                    ListenUrls = baseUrl,
                    DesktopAccessToken = desktopAccessToken ?? string.Empty,
                    ProductEdition = ProductEditionCatalog.Normalize(productEdition)
                };

                ApiStartupValidator.Validate(pathProvider, databaseSettings, runtimeOptions);

                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls(baseUrl);
                builder.Services.AddSingleton<IRuntimeLicenseAnchorStore>(
                    new FileRuntimeLicenseAnchorStore(
                        Path.Combine(dataRoot, "Security", "test-license-anchor.dat"),
                        "API 集成测试隔离授权锚点。"));
                if (licenseSignatureVerifier != null)
                {
                    builder.Services.AddSingleton(licenseSignatureVerifier);
                }
                builder.Services.AddExportDocManagerApiServices(pathProvider, databaseSettings, runtimeOptions);

                var app = builder.Build();
                app.UseCors(ApiCorsPolicy.LocalFrontendPolicyName);
                app.UseExportDocManagerDesktopAccess();
                app.UseExportDocManagerApiAuthentication();
                app.UseExportDocManagerWorkspaceAccess();
                app.UseExportDocManagerLicenseRequirement();
                app.MapExportDocManagerApiEndpoints(runtimeOptions, databaseSettings);
                await app.StartAsync();

                return new ApiIntegrationTestHarness(app, appRoot, dataRoot, databasePath, baseUrl);
            }
            catch
            {
                if (cleanupOnFailure)
                {
                    DeleteDirectoryIfExists(appRoot);
                    DeleteDirectoryIfExists(dataRoot);
                }

                throw;
            }
        }

        public HttpClient CreateClient(string accessToken = null, string desktopAccessToken = null)
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

        public async ValueTask DisposeAsync()
        {
            await StopAppAsync();
            DeleteDirectoryIfExists(AppRoot);
            DeleteDirectoryIfExists(DataRoot);
        }

        public async ValueTask StopAppAsync()
        {
            if (_disposed)
            {
                return;
            }

            await _app.StopAsync();
            await _app.DisposeAsync();
            _disposed = true;
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
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

            SqliteConnection.ClearAllPools();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    internal sealed class ApiTestLicenseSignatureVerifier : ILicenseSignatureVerifier
    {
        public const string ValidLicenseKey = "EDM2-API-TEST-LICENSE";
        public static readonly ApiTestLicenseSignatureVerifier Instance = new();

        public bool TryValidate(string machineId, string licenseKey, out DateTime expireDate)
        {
            expireDate = DateTime.Today.AddYears(1).Date.AddDays(1).AddTicks(-1);
            return !string.IsNullOrWhiteSpace(machineId) &&
                string.Equals(licenseKey, ValidLicenseKey, StringComparison.Ordinal);
        }
    }
}

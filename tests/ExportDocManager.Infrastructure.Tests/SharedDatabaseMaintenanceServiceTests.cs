using System.IO.Compression;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class SharedDatabaseMaintenanceServiceTests
{
    [Theory]
    [InlineData("pg_dump (PostgreSQL) 16.4", 16)]
    [InlineData("PostgreSQL 15.8 (Debian 15.8-1)", 15)]
    [InlineData("psql (PostgreSQL) 9.6.24", 9)]
    public void ParsePostgreSqlMajorVersion_ShouldReadToolAndServerFormats(string value, int expected)
    {
        Assert.Equal(expected, SharedDatabaseMaintenanceService.ParsePostgreSqlMajorVersion(value, "test"));
    }

    [Fact]
    public void EnsurePgDumpVersionSupported_ShouldRejectOlderClientMajorVersion()
    {
        var exception = Assert.Throws<InfrastructureServiceException>(() =>
            SharedDatabaseMaintenanceService.EnsurePgDumpVersionSupported(15, 16));

        Assert.Contains("低于", exception.Message, StringComparison.Ordinal);
        SharedDatabaseMaintenanceService.EnsurePgDumpVersionSupported(16, 16);
        SharedDatabaseMaintenanceService.EnsurePgDumpVersionSupported(17, 16);
    }

    [Fact]
    public void RestorePlanQuoting_ShouldPreserveArgumentsWithoutShellExpansion()
    {
        Assert.Equal("'C:\\Tools\\O''Brien\\pg_restore.exe'",
            SharedDatabaseMaintenanceService.QuotePowerShellLiteral("C:\\Tools\\O'Brien\\pg_restore.exe"));
        Assert.Equal("'/opt/o'\"'\"'brien/pg_restore'",
            SharedDatabaseMaintenanceService.QuotePosixShellArgument("/opt/o'brien/pg_restore"));
    }

    [Fact]
    public void PostRestoreOwnershipSql_ShouldHandleAllRoutineKindsAndRoleScopedDefaults()
    {
        string sql = SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
            "team_db",
            "app\"role",
            ["legacy_owner"]);

        Assert.Contains("p.prokind AS routine_kind", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER PROCEDURE", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER AGGREGATE", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER FUNCTION", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON ALL ROUTINES", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER DEFAULT PRIVILEGES FOR ROLE \"app\"\"role\"", sql, StringComparison.Ordinal);
        Assert.Contains("REASSIGN OWNED BY \"legacy_owner\" TO \"app\"\"role\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PostRestoreOwnershipSql_ShouldKeepCommentMetadataOnSingleCommentLines()
    {
        Assert.Throws<ServiceValidationException>(() =>
            SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
                "team_db\r\nGRANT ALL ON DATABASE postgres TO public;",
                "app_role\nDROP ROLE important_role;",
                []));

        string sql = SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
            "team_db",
            "app_role",
            []);

        Assert.Contains(
            "-- Target database: team_db",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "-- Application role: app_role",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPostRestoreOwnershipSql_ShouldBoundPostgreSqlIdentifiersAndRoleCount()
    {
        Assert.Throws<ServiceValidationException>(() =>
            SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
                new string('d', 64),
                "app_role",
                Array.Empty<string>()));

        Assert.Throws<ServiceValidationException>(() =>
            SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
                "business_db",
                "app_role",
                Enumerable.Range(0, 101).Select(index => $"owner_{index}").ToArray()));
    }

    [Fact]
    public async Task CreateSupportPackageAsync_ShouldIncludeOnlyTheBoundedLogTail()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"export-doc-manager-support-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var factory = new TestDbContextFactory();
            var pathProvider = new TestAppPathProvider(root, Path.Combine(root, "App_Data"));
            Directory.CreateDirectory(pathProvider.LogRoot);
            Directory.CreateDirectory(pathProvider.ConfigRoot);
            await File.WriteAllTextAsync(
                Path.Combine(pathProvider.ConfigRoot, "appsettings.json"),
                """
                {
                  "ConnectionStrings": {
                    "Default": "Host=localhost;Username=admin;Password=connection-secret"
                  },
                  "ExternalCredential": "credential-secret"
                }
                """);
            string logPath = Path.Combine(pathProvider.LogRoot, "oversized.log");
            await using (var stream = new FileStream(logPath, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] prefix = new byte[1024 * 1024];
                Array.Fill(prefix, (byte)0x11);
                await stream.WriteAsync(prefix);
                byte[] tail = new byte[8 * 1024 * 1024];
                Array.Fill(tail, (byte)0x7a);
                await stream.WriteAsync(tail);
            }

            var service = new SharedDatabaseMaintenanceService(
                factory,
                new DatabaseConnectionSettings(),
                pathProvider);
            SupportPackageResult result = await service.CreateSupportPackageAsync();

            using ZipArchive archive = ZipFile.OpenRead(result.FullPath);
            ZipArchiveEntry logEntry = Assert.Single(
                archive.Entries,
                entry => entry.FullName == "logs/oversized.log");
            Assert.Equal(8L * 1024 * 1024, logEntry.Length);
            await using Stream logStream = logEntry.Open();
            Assert.Equal(0x7a, logStream.ReadByte());

            ZipArchiveEntry indexEntry = Assert.Single(
                archive.Entries,
                entry => entry.FullName == "logs/log-index.json");
            using var reader = new StreamReader(indexEntry.Open());
            string indexJson = await reader.ReadToEndAsync();
            Assert.Contains("\"tailOnly\": true", indexJson, StringComparison.Ordinal);
            Assert.Contains("\"includedBytes\": 8388608", indexJson, StringComparison.Ordinal);

            ZipArchiveEntry settingsEntry = Assert.Single(
                archive.Entries,
                entry => entry.FullName == "diagnostics/settings-redacted.json");
            using var settingsReader = new StreamReader(settingsEntry.Open());
            string settingsJson = await settingsReader.ReadToEndAsync();
            Assert.DoesNotContain("connection-secret", settingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("credential-secret", settingsJson, StringComparison.Ordinal);
            Assert.Contains("***", settingsJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateSupportPackageAsync_ShouldKeepConcurrentExportsIndependent()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"export-doc-manager-support-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            using var factory = new TestDbContextFactory();
            var pathProvider = new TestAppPathProvider(root, Path.Combine(root, "App_Data"));
            var service = new SharedDatabaseMaintenanceService(
                factory,
                new DatabaseConnectionSettings(),
                pathProvider);

            SupportPackageResult[] results = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => service.CreateSupportPackageAsync()));

            Assert.Equal(results.Length, results.Select(result => result.FullPath).Distinct().Count());
            Assert.All(results, result =>
            {
                Assert.True(result.Success);
                Assert.True(File.Exists(result.FullPath));
                Assert.EndsWith("_support_package.zip", result.FileName, StringComparison.Ordinal);
                using ZipArchive archive = ZipFile.OpenRead(result.FullPath);
                Assert.NotNull(archive.GetEntry("diagnostics/runtime.json"));
                Assert.NotNull(archive.GetEntry("diagnostics/settings-redacted.json"));
            });

            Assert.Empty(Directory.EnumerateFiles(
                service.SupportPackageRoot,
                "*.tmp*",
                SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public void Dispose()
        {
            using AppDbContext context = CreateDbContext();
            context.Database.EnsureDeleted();
        }
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        public TestAppPathProvider(string appRoot, string dataRoot)
        {
            AppRoot = appRoot;
            DataRoot = dataRoot;
        }

        public string AppRoot { get; }
        public string DataRoot { get; }
        public string DatabaseRoot => Path.Combine(DataRoot, "Database");
        public string TemplateRoot => Path.Combine(AppRoot, "Templates");
        public string UserTemplateRoot => Path.Combine(DataRoot, "Templates");
        public string ResourceRoot => Path.Combine(AppRoot, "Resources");
        public string BrowserRoot => Path.Combine(AppRoot, "Browsers");
        public string ToolRoot => Path.Combine(AppRoot, "Tools");
        public string FileRoot => Path.Combine(DataRoot, "Files");
        public string ExportRoot => Path.Combine(DataRoot, "Exports");
        public string BackupRoot => Path.Combine(DataRoot, "Backups");
        public string SingleWindowRoot => Path.Combine(DataRoot, "SingleWindow");
        public string OcrModelRoot => Path.Combine(AppRoot, "OcrModels");
        public string LogRoot => Path.Combine(DataRoot, "Logs");
        public string CacheRoot => Path.Combine(DataRoot, "Cache");
        public string ConfigRoot => Path.Combine(DataRoot, "Config");
        public string SecurityRoot => Path.Combine(DataRoot, "Security");
        public string WebViewRoot => Path.Combine(DataRoot, "WebView");
    }
}

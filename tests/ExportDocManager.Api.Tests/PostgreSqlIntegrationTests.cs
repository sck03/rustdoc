using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Api.Tests
{
    public sealed class PostgreSqlIntegrationTests
    {
        [Fact]
        public async Task PostgreSql_RealServer_ShouldInitializePersistSessionsAndRejectStaleWrites()
        {
            string connectionString = Environment.GetEnvironmentVariable(
                "EXPORTDOC_TEST_POSTGRES_CONNECTION_STRING") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var settings = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = builder.Host,
                PostgreSqlPort = builder.Port,
                PostgreSqlDatabase = builder.Database,
                PostgreSqlUsername = builder.Username,
                PostgreSqlPassword = builder.Password
            };
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(DbHelper.BuildPostgreSqlConnectionString(settings))
                .Options;
            var factory = new PostgreSqlTestDbContextFactory(options);

            await using (var cleanup = factory.CreateDbContext())
            {
                await cleanup.Database.EnsureDeletedAsync();
            }

            try
            {
                var initialization = new DatabaseInitializationService(
                    factory,
                    settings,
                    new DatabaseInitializationCoordinator());
                var initialized = await initialization.InitializeAsync("admin", "postgres-test-admin");
                Assert.True(initialized.IsSuccess, initialized.ErrorMessage);

                User admin;
                await using (var context = factory.CreateDbContext())
                {
                    Assert.True(await context.Database.CanConnectAsync());
                    admin = await context.Users.AsNoTracking().SingleAsync(item => item.Username == "admin");
                    Assert.True(admin.IsActive);
                }

                var sessions = new DatabaseApiSessionTokenService(factory);
                var issued = await sessions.IssueAsync(admin);
                var validated = await sessions.ValidateAsync(issued.AccessToken);
                Assert.Equal(admin.Id, validated?.Id);
                await using (var context = factory.CreateDbContext())
                {
                    var stored = await context.ApiUserSessions.AsNoTracking().SingleAsync();
                    Assert.NotEqual(issued.AccessToken, stored.TokenHash);
                    Assert.Equal(64, stored.TokenHash.Length);
                }

                var crm = new CrmService(
                    factory,
                    new BusinessDataAccessScope(settings, new FixedCurrentUserContext(admin)));
                var created = await crm.SaveCustomerAsync(new CrmCustomerSaveRequest(
                    0, "PostgreSQL Customer", "CN", string.Empty, "潜在客户", "integration",
                    string.Empty, null));
                var updated = await crm.SaveCustomerAsync(new CrmCustomerSaveRequest(
                    created.Id, created.Name, created.CountryRegion, created.Website, "跟进中",
                    created.Source, created.Notes, created.LinkedDocumentCustomerId, created.VersionNumber));
                Assert.Equal(2, updated.VersionNumber);
                await Assert.ThrowsAsync<BusinessConcurrencyException>(() => crm.SaveCustomerAsync(
                    new CrmCustomerSaveRequest(created.Id, created.Name, created.CountryRegion, created.Website,
                        "暂停", created.Source, created.Notes, created.LinkedDocumentCustomerId,
                        created.VersionNumber)));
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
                await using var cleanup = factory.CreateDbContext();
                await cleanup.Database.EnsureDeletedAsync();
            }
        }

        private sealed class PostgreSqlTestDbContextFactory : IDbContextFactory<AppDbContext>
        {
            private readonly DbContextOptions<AppDbContext> _options;

            public PostgreSqlTestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

            public AppDbContext CreateDbContext() => new(_options);
        }

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User currentUser) => CurrentUser = currentUser;
            public User CurrentUser { get; }
        }
    }
}

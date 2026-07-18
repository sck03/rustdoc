using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class DataAccessCoreTests
    {
        [Fact]
        public void DatabaseConnectionSettings_ShouldKeepPortableSqliteDefaults()
        {
            var settings = new DatabaseConnectionSettings();

            Assert.Equal(DatabaseConnectionSettings.SqliteProvider, settings.Provider);
            Assert.Equal(DatabaseConnectionSettings.DefaultSqliteDatabaseFileName, settings.SqliteDatabaseFileName);
            Assert.Equal(DatabaseConnectionSettings.DefaultPostgreSqlPort, settings.PostgreSqlPort);
        }

        [Fact]
        public void SqliteConnectionString_ShouldUseStandardPortableSqliteWithoutPassword()
        {
            string databasePath = Path.Combine(Path.GetTempPath(), "exportdoc-standard-sqlite.db");
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
                DbHelper.BuildConnectionString(databasePath));

            Assert.Equal(databasePath, builder.DataSource);
            Assert.True(builder.ForeignKeys);
            Assert.Equal(10, builder.DefaultTimeout);
            Assert.DoesNotContain("Password", builder.ConnectionString, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DatabaseModeHelper_ShouldNormalizeAndValidateProvider()
        {
            Assert.Equal(DatabaseConnectionSettings.SqliteProvider, DatabaseModeHelper.NormalizeProvider(" sqlite "));
            Assert.Equal(DatabaseConnectionSettings.PostgreSqlProvider, DatabaseModeHelper.NormalizeProvider("postgresql"));

            var incompletePostgreSql = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "127.0.0.1"
            };

            Assert.Contains("PostgreSQL", DatabaseModeHelper.Validate(incompletePostgreSql));
        }

        [Fact]
        public void AppDbContext_Model_ShouldContainMainAndSingleWindowEntities()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var context = new AppDbContext(options);

            Assert.NotNull(context.Model.FindEntityType(typeof(Invoice)));
            Assert.NotNull(context.Model.FindEntityType(typeof(Item)));
            Assert.NotNull(context.Model.FindEntityType(typeof(CustomsCooDocument)));
            Assert.NotNull(context.Model.FindEntityType(typeof(SwSubmissionBatch)));
        }

        [Fact]
        public void AuditInterceptor_ShouldUseConfiguredAuditUserProvider()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(new AuditInterceptor(new FixedAuditUserProvider("alice")))
                .Options;

            using var context = new AppDbContext(options);

            context.Customers.Add(new Customer { CustomerNameEN = "Buyer" });
            context.SaveChanges();

            var auditLog = Assert.Single(context.AuditLogs);
            Assert.Equal("alice", auditLog.UserId);
            Assert.Equal(nameof(Customer), auditLog.EntityName);
            Assert.Equal(EntityState.Added.ToString(), auditLog.Action);
        }

        [Fact]
        public void AuditInterceptor_ShouldRedactPasswordsAndLargeTemplateContent()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(new AuditInterceptor(new FixedAuditUserProvider("admin")))
                .Options;

            using var context = new AppDbContext(options);
            context.Users.Add(new User
            {
                Username = "operator",
                PasswordHash = "super-secret-password-hash",
                Role = UserRoleCatalog.User
            });
            context.UserReportTemplates.Add(new UserReportTemplate
            {
                ReportType = "ExportDocument",
                Name = "商业发票",
                ContentHtml = new string('x', 3000)
            });
            context.SaveChanges();

            var userAudit = context.AuditLogs.Single(item => item.EntityName == nameof(User));
            Assert.Contains("[REDACTED]", userAudit.NewValues, StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret-password-hash", userAudit.NewValues, StringComparison.Ordinal);

            var templateAudit = context.AuditLogs.Single(item => item.EntityName == nameof(UserReportTemplate));
            Assert.Contains("[TEXT length=3000 sha256=", templateAudit.NewValues, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('x', 100), templateAudit.NewValues, StringComparison.Ordinal);
        }

        [Fact]
        public void DbSeeder_ShouldSeedSqliteAdminAndReferenceData()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var context = new AppDbContext(options);

            DbSeeder.SeedAuxiliaryData(context, new DatabaseConnectionSettings());

            var admin = Assert.Single(context.Users);
            Assert.Equal("admin", admin.Username);
            Assert.Equal(UserRoleCatalog.Admin, admin.Role);
            Assert.True(PasswordHasher.VerifyPassword(admin.PasswordHash, string.Empty));
            Assert.NotEmpty(context.Units);
            Assert.NotEmpty(context.Ports);
            Assert.NotEmpty(context.ContainerTypeDefinitions);
        }

        [Fact]
        public void DbSeeder_WhenPostgreSqlWithoutInitialPassword_ShouldRequirePassword()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var context = new AppDbContext(options);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                DbSeeder.SeedAuxiliaryData(
                    context,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                        PostgreSqlHost = "127.0.0.1",
                        PostgreSqlDatabase = "exportdoc",
                        PostgreSqlUsername = "admin"
                    }));

            Assert.Contains("只能使用 admin 账号登录", exception.Message);
        }

        [Fact]
        public void DbSeeder_WhenPostgreSqlInitialPasswordIsTooShort_ShouldRejectPassword()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var context = new AppDbContext(options);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                DbSeeder.SeedAuxiliaryData(
                    context,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                        PostgreSqlHost = "127.0.0.1",
                        PostgreSqlDatabase = "exportdoc",
                        PostgreSqlUsername = "admin"
                    },
                    "short"));

            Assert.Contains("至少需要 8 个字符", exception.Message);
        }

        [Fact]
        public void DatabaseInitialization_ShouldOnlyUseFirstLoginPasswordForAdminPostgreSqlBootstrap()
        {
            Assert.Equal(
                "first-admin-password",
                DatabaseInitializationService.ResolveInitialAdminPassword(
                    usesPostgreSql: true,
                    username: " ADMIN ",
                    password: "first-admin-password"));
            Assert.Equal(
                string.Empty,
                DatabaseInitializationService.ResolveInitialAdminPassword(
                    usesPostgreSql: true,
                    username: "operator",
                    password: "operator-password"));
            Assert.Equal(
                string.Empty,
                DatabaseInitializationService.ResolveInitialAdminPassword(
                    usesPostgreSql: false,
                    username: "admin",
                    password: "desktop-database-password"));
        }

        [Fact]
        public void PasswordHasher_ShouldUseCurrentWorkFactorAndRejectMalformedHash()
        {
            string hash = PasswordHasher.HashPassword("valid-password");

            Assert.StartsWith("210000.", hash, StringComparison.Ordinal);
            Assert.True(PasswordHasher.VerifyPassword(hash, "valid-password"));
            Assert.False(PasswordHasher.VerifyPassword(hash, "wrong-password"));
            Assert.False(PasswordHasher.VerifyPassword("broken-hash", "valid-password"));
        }

        [Fact]
        public async Task DatabaseInitializationService_ShouldUpgradeLegacyInvoiceNoUniqueIndexToTypeAwareIndex()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                "edm-invoice-type-schema-" + Guid.NewGuid().ToString("N") + ".db");
            using var factory = new SqliteFileDbContextFactory(databasePath);

            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.EnsureCreatedAsync();
                await context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS \"IX_Invoices_InvoiceNo_Type\"");
                await context.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX \"IX_Invoices_InvoiceNo\" ON \"Invoices\" (\"InvoiceNo\")");

                context.Invoices.Add(new Invoice
                {
                    InvoiceNo = "INV-TYPE-UPGRADE",
                    InvoiceDate = new DateTime(2026, 6, 25),
                    ShipmentDate = new DateTime(2026, 6, 25)
                });
                await context.SaveChangesAsync();
            }

            var service = new DatabaseInitializationService(
                factory,
                new DatabaseConnectionSettings(),
                new DatabaseInitializationCoordinator());

            var result = await service.InitializeAsync("admin", string.Empty);

            Assert.True(result.IsSuccess, result.ErrorMessage);

            await using var verifyContext = await factory.CreateDbContextAsync();
            var existing = await verifyContext.Invoices.SingleAsync();
            Assert.Equal("实际数据", existing.Type);

            verifyContext.Invoices.Add(new Invoice
            {
                InvoiceNo = "INV-TYPE-UPGRADE",
                Type = "报关数据",
                InvoiceDate = new DateTime(2026, 6, 25),
                ShipmentDate = new DateTime(2026, 6, 25)
            });
            await verifyContext.SaveChangesAsync();

            verifyContext.Invoices.Add(new Invoice
            {
                InvoiceNo = "INV-TYPE-UPGRADE",
                Type = "报关数据",
                InvoiceDate = new DateTime(2026, 6, 26),
                ShipmentDate = new DateTime(2026, 6, 26)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => verifyContext.SaveChangesAsync());
        }

        private sealed class FixedAuditUserProvider : IAuditUserProvider
        {
            private readonly string _userName;

            public FixedAuditUserProvider(string userName)
            {
                _userName = userName;
            }

            public string GetCurrentUserName()
            {
                return _userName;
            }
        }

        private sealed class SqliteFileDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly string _databasePath;
            private readonly DbContextOptions<AppDbContext> _options;

            public SqliteFileDbContextFactory(string databasePath)
            {
                _databasePath = databasePath;
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={databasePath}")
                    .Options;
            }

            public AppDbContext CreateDbContext()
            {
                return new AppDbContext(_options);
            }

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CreateDbContext());
            }

            public void Dispose()
            {
                using var context = CreateDbContext();
                context.Database.EnsureDeleted();
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
        }
    }
}

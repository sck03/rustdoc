using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

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
        public void PostgreSqlConnectionString_ShouldBoundPoolForMultiUserLoad()
        {
            string previous = Environment.GetEnvironmentVariable(DbHelper.PostgreSqlMaximumPoolSizeEnvironmentVariable);
            Environment.SetEnvironmentVariable(DbHelper.PostgreSqlMaximumPoolSizeEnvironmentVariable, "24");
            try
            {
                string connectionString = DbHelper.BuildPostgreSqlConnectionString(new DatabaseConnectionSettings
                {
                    Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                    PostgreSqlHost = "127.0.0.1",
                    PostgreSqlDatabase = "exportdoc",
                    PostgreSqlUsername = "exportdoc",
                    PostgreSqlPassword = "secret"
                });
                var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

                Assert.True(builder.Pooling);
                Assert.Equal(2, builder.MinPoolSize);
                Assert.Equal(24, builder.MaxPoolSize);
                Assert.Equal(300, builder.ConnectionIdleLifetime);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DbHelper.PostgreSqlMaximumPoolSizeEnvironmentVariable, previous);
            }
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
        public void AppDbContext_Model_ShouldRestrictSupplierHistoryDeletion()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            using var context = new AppDbContext(options);

            var productLinkForeignKey = Assert.Single(
                context.Model.FindEntityType(typeof(SupplierProductLink))!.GetForeignKeys(),
                foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(SupplierCompany));
            var assessmentForeignKey = Assert.Single(
                context.Model.FindEntityType(typeof(SupplierAssessment))!.GetForeignKeys(),
                foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(SupplierCompany));
            var contactForeignKey = Assert.Single(
                context.Model.FindEntityType(typeof(SupplierContact))!.GetForeignKeys(),
                foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(SupplierCompany));

            Assert.Equal(DeleteBehavior.Restrict, contactForeignKey.DeleteBehavior);
            Assert.Equal(DeleteBehavior.Restrict, productLinkForeignKey.DeleteBehavior);
            Assert.Equal(DeleteBehavior.Restrict, assessmentForeignKey.DeleteBehavior);
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
        public void AuditInterceptor_ShouldFinalizeDatabaseGeneratedEntityId()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new AuditInterceptor(new FixedAuditUserProvider("admin")))
                .Options;

            using var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            var customer = new Customer { CustomerNameEN = "Generated key customer" };

            context.Customers.Add(customer);
            context.SaveChanges();

            var auditLog = context.AuditLogs.Single(item => item.EntityName == nameof(Customer));
            Assert.Equal(customer.Id.ToString(), auditLog.EntityId);
            Assert.DoesNotContain("generated-after-save", auditLog.EntityId, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AppDbContext_ShouldEnforceOnePrimaryContactPerCrmCustomerAndSupplier()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            using var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            var customer = new CrmCustomer { Name = "客户" };
            var supplier = new SupplierCompany { Name = "供应商", Status = "合作中" };
            context.CrmCustomers.Add(customer);
            context.SupplierCompanies.Add(supplier);
            context.SaveChanges();

            context.CrmContacts.AddRange(
                new CrmContact { CrmCustomerId = customer.Id, Name = "甲", IsPrimary = true },
                new CrmContact { CrmCustomerId = customer.Id, Name = "乙", IsPrimary = true });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            context.ChangeTracker.Clear();

            context.SupplierContacts.AddRange(
                new SupplierContact { SupplierCompanyId = supplier.Id, Name = "甲", IsPrimary = true },
                new SupplierContact { SupplierCompanyId = supplier.Id, Name = "乙", IsPrimary = true });
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
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
        public void DbSeeder_ShouldSeedSqliteAdminWithEmptyPasswordAndReferenceData()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var context = new AppDbContext(options);

            const string suppliedPassword = "desktop-password-must-be-ignored";
            DbSeeder.SeedAuxiliaryData(context, new DatabaseConnectionSettings(), suppliedPassword);

            var admin = Assert.Single(context.Users);
            Assert.Equal("admin", admin.Username);
            Assert.Equal(UserRoleCatalog.Admin, admin.Role);
            Assert.True(PasswordHasher.VerifyPassword(admin.PasswordHash, string.Empty));
            Assert.False(PasswordHasher.VerifyPassword(admin.PasswordHash, suppliedPassword));
            Assert.NotEmpty(context.Units);
            Assert.NotEmpty(context.Ports);
            Assert.NotEmpty(context.ContainerTypeDefinitions);
        }

        [Fact]
        public void DbSeeder_WhenPostgreSqlHasNoInitialPassword_ShouldRequirePassword()
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

            var exception = Assert.Throws<ServiceValidationException>(() =>
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
        public void DatabaseInitialization_ShouldOnlyUsePostgreSqlAdminPasswordForBootstrap()
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
        public async Task DatabaseInitializationService_ShouldCreateCurrentVersionedSchemaForEmptyDatabase()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                "edm-v6-schema-" + Guid.NewGuid().ToString("N") + ".db");
            using var factory = new SqliteFileDbContextFactory(databasePath);

            var service = new DatabaseInitializationService(
                factory,
                new DatabaseConnectionSettings(),
                new DatabaseInitializationCoordinator());

            const string suppliedPassword = "desktop-password-must-be-ignored";
            var result = await service.InitializeAsync("admin", suppliedPassword);

            Assert.True(result.IsSuccess, result.ErrorMessage);

            await using var verifyContext = await factory.CreateDbContextAsync();
            int schemaVersion = await verifyContext.Database
                .SqlQueryRaw<int>("SELECT \"Version\" AS \"Value\" FROM \"__ExportDocManagerSchema\" WHERE \"Id\" = 1")
                .SingleAsync();
            Assert.Equal(6, schemaVersion);

            var admin = await verifyContext.Users.SingleAsync(user => user.Username == "admin");
            Assert.True(PasswordHasher.VerifyPassword(admin.PasswordHash, string.Empty));
            Assert.False(PasswordHasher.VerifyPassword(admin.PasswordHash, suppliedPassword));

            verifyContext.Invoices.Add(new Invoice
            {
                InvoiceNo = "INV-V1-TYPE",
                Type = "报关数据",
                InvoiceDate = new DateTime(2026, 6, 25),
                ShipmentDate = new DateTime(2026, 6, 25)
            });
            await verifyContext.SaveChangesAsync();

            verifyContext.Invoices.Add(new Invoice
            {
                InvoiceNo = "INV-V1-TYPE",
                Type = "报关数据",
                InvoiceDate = new DateTime(2026, 6, 26),
                ShipmentDate = new DateTime(2026, 6, 26)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => verifyContext.SaveChangesAsync());
        }

        [Fact]
        public async Task DatabaseInitializationService_ShouldRejectUnversionedPreReleaseDatabase()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                "edm-unversioned-schema-" + Guid.NewGuid().ToString("N") + ".db");
            using var factory = new SqliteFileDbContextFactory(databasePath);
            await using (var context = await factory.CreateDbContextAsync())
            {
                await context.Database.EnsureCreatedAsync();
            }

            var service = new DatabaseInitializationService(
                factory,
                new DatabaseConnectionSettings(),
                new DatabaseInitializationCoordinator());

            var result = await service.InitializeAsync("admin", string.Empty);

            Assert.False(result.IsSuccess);
            Assert.Contains("无版本标记", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains("空数据库重新初始化", result.ErrorMessage, StringComparison.Ordinal);
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

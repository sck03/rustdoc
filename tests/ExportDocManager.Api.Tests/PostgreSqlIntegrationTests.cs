using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;

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

            await ResetPublicSchemaAsync(connectionString);

            try
            {
                var initialization = new DatabaseInitializationService(
                    factory,
                    settings,
                    new DatabaseInitializationCoordinator());
                var initialized = await initialization.InitializeAsync("admin", "postgres-test-admin");
                Assert.True(initialized.IsSuccess, initialized.ErrorMessage);

                var indexNames = await LoadPublicIndexNamesAsync(connectionString);
                foreach (string requiredIndex in new[]
                {
                    "IX_HsCodes_Status_NormalizedCode_Prefix",
                    "IX_HsCodeDeclarationExamples_RawCode_Prefix",
                    "IX_HsCodeRemoteCandidates_Status_RawCode_Prefix",
                    "IX_HsCodes_TextSearch_Trgm",
                    "IX_HsCodeDeclarationExamples_TextSearch_Trgm",
                    "IX_Items_HistorySearch_Trgm"
                })
                {
                    Assert.Contains(requiredIndex, indexNames);
                }
                Assert.True(await PostgreSqlExtensionExistsAsync(connectionString, "pg_trgm"));

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

                var now = DateTime.UtcNow;
                var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                await using (var context = factory.CreateDbContext())
                {
                    context.Invoices.AddRange(
                        new Invoice
                        {
                            InvoiceNo = "PG-DASH-DUP",
                            Type = "报关数据",
                            Status = InvoiceStatusCatalog.Verified,
                            InvoiceDate = startOfMonth.AddDays(1),
                            ShipmentDate = startOfMonth.AddDays(1),
                            TotalAmount = 40m,
                            TotalProfit = 4m,
                            TotalTaxRefundAmount = 2m
                        },
                        new Invoice
                        {
                            InvoiceNo = "PG-DASH-DUP",
                            Type = "实际数据",
                            Status = InvoiceStatusCatalog.Shipped,
                            InvoiceDate = startOfMonth.AddDays(2),
                            ShipmentDate = startOfMonth.AddDays(2),
                            TotalAmount = 100m,
                            TotalProfit = 10m,
                            TotalTaxRefundAmount = 5m
                        },
                        new Invoice
                        {
                            InvoiceNo = "PG-DASH-DRAFT",
                            Type = "实际数据",
                            Status = InvoiceStatusCatalog.Draft,
                            InvoiceDate = startOfMonth.AddDays(3),
                            ShipmentDate = startOfMonth.AddDays(3),
                            TotalAmount = 20m,
                            TotalProfit = 2m,
                            TotalTaxRefundAmount = 1m
                        },
                        new Invoice
                        {
                            InvoiceNo = "PG-DASH-PREVIOUS",
                            Type = "实际数据",
                            Status = InvoiceStatusCatalog.Completed,
                            InvoiceDate = startOfMonth.AddDays(-1),
                            ShipmentDate = startOfMonth.AddDays(-1),
                            TotalAmount = 50m,
                            TotalProfit = 5m,
                            TotalTaxRefundAmount = 2.5m
                        });
                    await context.SaveChangesAsync();
                }

                var dashboard = new DashboardService(
                    factory,
                    new BusinessDataAccessScope(settings, new FixedCurrentUserContext(admin)));
                var snapshot = await dashboard.GetDashboardAsync();
                Assert.Equal(120m, snapshot.MonthlyExportAmount);
                Assert.Equal(50m, snapshot.PreviousMonthlyExportAmount);
                Assert.Equal(2, snapshot.MonthlyInvoiceCount);
                Assert.Equal(1, snapshot.DraftCount);
                Assert.Equal(1, snapshot.ShippedCount);
                Assert.Equal(1, snapshot.CompletedCount);
                Assert.Equal(3, snapshot.TotalActiveCount);

                var queryRepository = new LocalSharedReadRepository(
                    factory,
                    settings,
                    new BusinessDataAccessScope(settings, new FixedCurrentUserContext(admin)));
                var queryPage = await queryRepository.QueryPageAsync(new QueryPageQuery
                {
                    StartDate = DateTime.SpecifyKind(startOfMonth.AddDays(1), DateTimeKind.Unspecified),
                    EndDate = DateTime.SpecifyKind(startOfMonth.AddDays(3), DateTimeKind.Unspecified),
                    PageNumber = 1,
                    PageSize = 10
                });
                Assert.Equal(3, queryPage.TotalCount);
                Assert.DoesNotContain(queryPage.Items, invoice => invoice.InvoiceNo == "PG-DASH-PREVIOUS");
            }
            finally
            {
                await ResetPublicSchemaAsync(connectionString);
                NpgsqlConnection.ClearAllPools();
            }
        }

        [Fact]
        public async Task PostgreSql_CapacityDataset_ShouldKeepHsAndInvoiceQueriesBounded()
        {
            string connectionString = Environment.GetEnvironmentVariable(
                "EXPORTDOC_TEST_POSTGRES_CONNECTION_STRING") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            int hsRecordCount = ResolveCapacityCount("EXPORTDOC_TEST_HS_RECORDS", 10_000);
            int invoiceRecordCount = ResolveCapacityCount("EXPORTDOC_TEST_INVOICE_RECORDS", 10_000);
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

            await ResetPublicSchemaAsync(connectionString);
            try
            {
                var initialization = new DatabaseInitializationService(
                    factory,
                    settings,
                    new DatabaseInitializationCoordinator());
                var initialized = await initialization.InitializeAsync("admin", "postgres-capacity-admin");
                Assert.True(initialized.IsSuccess, initialized.ErrorMessage);

                await SeedCapacityDataAsync(connectionString, hsRecordCount, invoiceRecordCount);
                var knowledge = new HsCodeKnowledgeService(factory);
                var stopwatch = Stopwatch.StartNew();

                var prefixResults = await knowledge.SearchAsync("610000", 20);
                Assert.NotEmpty(prefixResults.Items);
                Assert.All(prefixResults.Items, item => Assert.StartsWith("610000", item.CurrentCode));

                var textResults = await knowledge.SearchAsync("CAPACITYMATCH", 20);
                Assert.NotEmpty(textResults.Items);
                Assert.InRange(textResults.Items.Count, 1, 20);

                int exampleMatches = await knowledge.CountExamplesAsync("CAPACITYMATCH");
                Assert.True(exampleMatches > 0);
                var examplePage = await knowledge.ListExamplesAsync("CAPACITYMATCH", 2, 30);
                Assert.InRange(examplePage.Count, 0, 30);

                var historyPage = await knowledge.DiscoverHistoryCandidatesAsync(
                    "CAPACITY-MATCH",
                    pageNumber: 1,
                    pageSize: 30);
                Assert.InRange(historyPage.Items.Count, 1, 30);
                Assert.True(historyPage.ScannedSourceCount <= 15_000);

                var injectionProbe = await knowledge.SearchAsync("' OR 1=1 --", 20);
                Assert.InRange(injectionProbe.Items.Count, 0, 20);

                await using (var context = factory.CreateDbContext())
                {
                    var invoicePage = await context.Invoices.AsNoTracking()
                        .Where(item => item.CompanyScope == "CAPACITY")
                        .OrderByDescending(item => item.InvoiceDate)
                        .ThenByDescending(item => item.Id)
                        .Skip(200)
                        .Take(50)
                        .Select(item => item.InvoiceNo)
                        .ToListAsync();
                    Assert.Equal(Math.Min(50, Math.Max(invoiceRecordCount - 200, 0)), invoicePage.Count);
                }

                stopwatch.Stop();
                var budget = hsRecordCount >= 1_000_000 || invoiceRecordCount >= 1_000_000
                    ? TimeSpan.FromSeconds(60)
                    : TimeSpan.FromSeconds(30);
                Assert.True(
                    stopwatch.Elapsed < budget,
                    $"Capacity queries exceeded {budget.TotalSeconds:N0}s: {stopwatch.Elapsed}. " +
                    $"HS={hsRecordCount:N0}, invoices={invoiceRecordCount:N0}.");
                Console.WriteLine(
                    $"Capacity validation completed in {stopwatch.Elapsed}. " +
                    $"HS={hsRecordCount:N0}, invoices={invoiceRecordCount:N0}, examples={exampleMatches:N0}.");
            }
            finally
            {
                await ResetPublicSchemaAsync(connectionString);
                NpgsqlConnection.ClearAllPools();
            }
        }

        private static int ResolveCapacityCount(string environmentName, int defaultValue)
        {
            string rawValue = Environment.GetEnvironmentVariable(environmentName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return defaultValue;
            }

            if (!int.TryParse(rawValue, out int value) || value is < 1_000 or > 1_000_000)
            {
                throw new InvalidOperationException(
                    $"{environmentName} must be an integer between 1,000 and 1,000,000.");
            }

            return value;
        }

        private static async Task SeedCapacityDataAsync(
            string connectionString,
            int hsRecordCount,
            int invoiceRecordCount)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 900;
            command.CommandText = """
                INSERT INTO "HsCodes" (
                    "Code", "NormalizedCode", "Name", "Description", "Elements", "Status",
                    "SourceName", "EffectiveYear", "LastVerifiedAt", "UpdateTime")
                SELECT
                    (6100000000::bigint + value)::text,
                    (6100000000::bigint + value)::text,
                    CASE WHEN value % 997 = 0
                        THEN 'CAPACITYMATCH化纤制针织女式套头衫'
                        ELSE '容量税则商品' || value::text END,
                    CASE WHEN value % 997 = 0 THEN 'CAPACITYMATCH出口测试描述' ELSE '年度税则容量样本' END,
                    CASE WHEN value % 997 = 0 THEN 'CAPACITYMATCH材质用途' ELSE '申报要素' END,
                    'Active',
                    'PostgreSQL capacity validation',
                    2026,
                    TIMESTAMPTZ '2026-07-27 00:00:00+00',
                    TIMESTAMPTZ '2026-07-27 00:00:00+00'
                FROM generate_series(1, @hsRecordCount) AS value;

                INSERT INTO "HsCodeDeclarationExamples" (
                    "Fingerprint", "RawReportedHsCode", "ResolvedCurrentHsCode", "ProductName",
                    "Specification", "SearchText", "Source", "SourceYear", "ResolutionStatus",
                    "IsManuallyVerified", "UseCount", "RejectedCount", "CreatedAt", "UpdatedAt")
                SELECT
                    lpad(value::text, 64, '0'),
                    (6100000000::bigint + value)::text,
                    (6100000000::bigint + value)::text,
                    CASE WHEN value % 997 = 0
                        THEN 'CAPACITYMATCH历史确认商品'
                        ELSE '容量申报实例' || value::text END,
                    CASE WHEN value % 997 = 0 THEN 'CAPACITYMATCH规格成分' ELSE '常规规格' END,
                    CASE WHEN value % 997 = 0
                        THEN 'CAPACITYMATCH历史确认商品CAPACITYMATCH规格成分'
                        ELSE '容量申报实例' || value::text || '常规规格' END,
                    'CapacityValidation',
                    2026,
                    'ManuallyVerified',
                    TRUE,
                    1,
                    0,
                    TIMESTAMPTZ '2026-07-27 00:00:00+00',
                    TIMESTAMPTZ '2026-07-27 00:00:00+00'
                FROM generate_series(1, @hsRecordCount) AS value;

                INSERT INTO "Invoices" (
                    "CompanyScope", "DepartmentId", "InvoiceNo", "Type", "Status", "InvoiceDate",
                    "ShipmentDate", "ExporterId", "CustomerId", "TotalCartons", "TotalQuantity",
                    "TotalGrossWeight", "TotalNetWeight", "TotalVolume", "TotalAmount",
                    "TotalPurchaseAmount", "TotalTaxRefundAmount", "TotalProfit")
                SELECT
                    'CAPACITY',
                    'DOCUMENT',
                    'CAP-' || value::text,
                    '实际数据',
                    'Draft',
                    DATE '2026-07-27' - ((value % 365)::integer),
                    DATE '2026-07-27',
                    0,
                    0,
                    1,
                    10,
                    5,
                    4,
                    0.025,
                    100,
                    70,
                    5,
                    35
                FROM generate_series(1, @invoiceRecordCount) AS value;

                INSERT INTO "Items" (
                    "InvoiceId", "StyleNo", "StyleName", "StyleNameCN", "FabricComposition", "Brand",
                    "HSCode", "Quantity", "PcsPerCtn", "Cartons", "Length", "Width", "Height", "Volume",
                    "GWPerCtn", "NWPerCtn", "GWTotal", "NWTotal", "PriceCalculationMode", "UnitPrice",
                    "TotalPrice", "PurchasePrice", "PurchaseTotal", "TaxRebateRate")
                SELECT
                    invoice."Id",
                    CASE WHEN invoice."Id" % 997 = 0
                        THEN 'CAPACITY-MATCH-' || invoice."Id"::text
                        ELSE 'STYLE-' || invoice."Id"::text END,
                    CASE WHEN invoice."Id" % 997 = 0 THEN 'CAPACITY-MATCH KNITTED PULLOVER' ELSE 'CAPACITY ITEM' END,
                    CASE WHEN invoice."Id" % 997 = 0 THEN 'CAPACITY-MATCH化纤针织套头衫' ELSE '容量商品' END,
                    CASE WHEN invoice."Id" % 997 = 0 THEN 'CAPACITY-MATCH聚酯纤维' ELSE '聚酯纤维' END,
                    'NO BRAND',
                    '6100000001',
                    10,
                    10,
                    1,
                    50,
                    40,
                    25,
                    0.05,
                    5,
                    4,
                    5,
                    4,
                    'UnitPrice',
                    10,
                    100,
                    7,
                    70,
                    13
                FROM "Invoices" AS invoice
                WHERE invoice."CompanyScope" = 'CAPACITY';

                ANALYZE "HsCodes";
                ANALYZE "HsCodeDeclarationExamples";
                ANALYZE "Invoices";
                ANALYZE "Items";
                """;
            command.Parameters.AddWithValue("hsRecordCount", hsRecordCount);
            command.Parameters.AddWithValue("invoiceRecordCount", invoiceRecordCount);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<HashSet<string>> LoadPublicIndexNamesAsync(string connectionString)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        private static async Task<bool> PostgreSqlExtensionExistsAsync(
            string connectionString,
            string extensionName)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = @extensionName)";
            command.Parameters.AddWithValue("extensionName", extensionName);
            return (bool)(await command.ExecuteScalarAsync() ?? false);
        }

        private static async Task ResetPublicSchemaAsync(string connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP SCHEMA IF EXISTS public CASCADE;
                CREATE SCHEMA public;
                GRANT ALL ON SCHEMA public TO PUBLIC;
                """;
            await command.ExecuteNonQueryAsync();
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

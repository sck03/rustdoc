using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class SharedReadRepositoryTests
    {
        [Fact]
        public async Task LocalSharedReadRepository_WhenPostgreSqlRegularUser_ShouldFilterOwnedRows()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Invoices.AddRange(
                    new Invoice { InvoiceNo = "OWN-INV", Type = "实际数据", OwnerUserId = 7, InvoiceDate = new DateOnly(2026, 4, 1), ShipmentDate = new DateOnly(2026, 4, 1) },
                    new Invoice { InvoiceNo = "OTHER-INV", Type = "实际数据", OwnerUserId = 8, InvoiceDate = new DateOnly(2026, 4, 2), ShipmentDate = new DateOnly(2026, 4, 2) });
                context.Payments.AddRange(
                    new Payment { InvoiceNo = "OWN-PAY", OwnerUserId = 7, PaymentDate = new DateOnly(2026, 4, 1) },
                    new Payment { InvoiceNo = "OTHER-PAY", OwnerUserId = 8, PaymentDate = new DateOnly(2026, 4, 2) });
                await context.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var accessScope = new BusinessDataAccessScope(
                settings,
                new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" }));
            var repository = new LocalSharedReadRepository(factory, accessScope);
            int ownPaymentId;
            int otherPaymentId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                ownPaymentId = await context.Payments
                    .Where(payment => payment.InvoiceNo == "OWN-PAY")
                    .Select(payment => payment.Id)
                    .SingleAsync();
                otherPaymentId = await context.Payments
                    .Where(payment => payment.InvoiceNo == "OTHER-PAY")
                    .Select(payment => payment.Id)
                    .SingleAsync();
            }

            var invoices = await repository.QueryPageAsync(new InvoiceListPageQuery { PageNumber = 1, PageSize = 10 });
            var payments = await repository.QueryPageAsync(new PaymentPageQuery { PageNumber = 1, PageSize = 10 });
            var ownPaymentDetail = await repository.GetByIdAsync(ownPaymentId);
            var foreignPaymentDetail = await repository.GetByIdAsync(otherPaymentId);

            var invoice = Assert.Single(invoices.Items);
            var payment = Assert.Single(payments.Items);
            Assert.Equal("OWN-INV", invoice.InvoiceNo);
            Assert.Equal("OWN-PAY", payment.InvoiceNo);
            Assert.Equal("OWN-PAY", Assert.IsType<Payment>(ownPaymentDetail).InvoiceNo);
            Assert.Null(foreignPaymentDetail);
        }

        [Fact]
        public async Task LocalSharedReadRepository_WhenPostgreSqlAdmin_ShouldReturnAllRows()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Invoices.AddRange(
                    new Invoice { InvoiceNo = "ADMIN-OWN", Type = "实际数据", OwnerUserId = 1, InvoiceDate = new DateOnly(2026, 4, 1), ShipmentDate = new DateOnly(2026, 4, 1) },
                    new Invoice { InvoiceNo = "ADMIN-OTHER", Type = "实际数据", OwnerUserId = 2, InvoiceDate = new DateOnly(2026, 4, 2), ShipmentDate = new DateOnly(2026, 4, 2) });
                await context.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var accessScope = new BusinessDataAccessScope(
                settings,
                new FixedCurrentUserContext(new User { Id = 1, Username = "admin", Role = "Admin" }));
            var repository = new LocalSharedReadRepository(factory, accessScope);

            var invoices = await repository.QueryPageAsync(new InvoiceListPageQuery { PageNumber = 1, PageSize = 10 });

            Assert.Equal(2, invoices.TotalCount);
            Assert.Equal(["ADMIN-OTHER", "ADMIN-OWN"], invoices.Items.Select(invoice => invoice.InvoiceNo).ToArray());
        }

        [Fact]
        public async Task QueryExportBatch_ShouldProjectStableScopedRowsWithoutRepeatedCounts()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Invoices.AddRange(
                    new Invoice { InvoiceNo = "OLDER", ContractNo = "C-1", Type = "实际数据", OwnerUserId = 7, InvoiceDate = new DateOnly(2026, 4, 1), ShipmentDate = new DateOnly(2026, 4, 1), TotalAmount = 10 },
                    new Invoice { InvoiceNo = "NEWER", ContractNo = "C-2", Type = "实际数据", OwnerUserId = 7, InvoiceDate = new DateOnly(2026, 4, 2), ShipmentDate = new DateOnly(2026, 4, 2), TotalAmount = 20 },
                    new Invoice { InvoiceNo = "FOREIGN", ContractNo = "C-3", Type = "实际数据", OwnerUserId = 8, InvoiceDate = new DateOnly(2026, 4, 3), ShipmentDate = new DateOnly(2026, 4, 3), TotalAmount = 30 });
                await context.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var repository = new LocalSharedReadRepository(
                factory,
                new BusinessDataAccessScope(
                    settings,
                    new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" })));
            var query = new QueryPageQuery();

            int count = await repository.CountAsync(query);
            var first = await repository.QueryExportBatchAsync(query, skip: 0, take: 1);
            var second = await repository.QueryExportBatchAsync(query, skip: 1, take: 1);

            Assert.Equal(2, count);
            Assert.Equal("NEWER", Assert.Single(first).InvoiceNo);
            Assert.Equal("OLDER", Assert.Single(second).InvoiceNo);
            Assert.Equal("2026-04-02", first[0].InvoiceDate);
            Assert.Equal(20, first[0].TotalAmount);
        }

        [Theory]
        [InlineData("%", "INV-100%")]
        [InlineData("_", "INV_STYLE")]
        [InlineData("\\", "PATH\\DOC")]
        [InlineData("alpha", "ALPHA-CASE")]
        public async Task InvoiceKeywordSearch_ShouldTreatLikeMetacharactersAsLiterals(
            string keyword,
            string expectedInvoiceNo)
        {
            using var factory = new SqliteTestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Invoices.AddRange(
                    CreateInvoice("INV-100%", 1),
                    CreateInvoice("INV-100X", 2),
                    CreateInvoice("INV_STYLE", 3),
                    CreateInvoice("INVXSTYLE", 4),
                    CreateInvoice("PATH\\DOC", 5),
                    CreateInvoice("PATHXDOC", 6),
                    CreateInvoice("ALPHA-CASE", 7));
                await context.SaveChangesAsync();
            }

            var repository = new LocalSharedReadRepository(factory, TestAccessScope.Create());

            var result = await repository.QueryPageAsync(new InvoiceListPageQuery
            {
                Keyword = keyword,
                PageNumber = 1,
                PageSize = 20
            });

            var invoice = Assert.Single(result.Items);
            Assert.Equal(expectedInvoiceNo, invoice.InvoiceNo);
        }

        [Fact]
        public async Task QueryKeywordSearch_ShouldTrackInvoiceItemChangesThroughSqliteFts()
        {
            using var factory = new SqliteTestDbContextFactory();
            int invoiceId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                var invoice = CreateInvoice("ITEM-SEARCH", 8);
                invoice.Items.Add(new Item
                {
                    StyleNo = "FTS-001",
                    StyleName = "Unique Jacket",
                    HSCode = "620100"
                });
                context.Invoices.Add(invoice);
                await context.SaveChangesAsync();
                invoiceId = invoice.Id;
            }

            var repository = new LocalSharedReadRepository(factory, TestAccessScope.Create());
            var initial = await repository.QueryPageAsync(new QueryPageQuery
            {
                Keyword = "unique jacket",
                PageNumber = 1,
                PageSize = 20
            });
            Assert.Equal("ITEM-SEARCH", Assert.Single(initial.Items).InvoiceNo);

            await using (var context = await factory.CreateDbContextAsync())
            {
                var item = await context.Items.SingleAsync(row => row.InvoiceId == invoiceId);
                item.StyleName = "Renamed Coat";
                await context.SaveChangesAsync();
            }

            var stale = await repository.QueryPageAsync(new QueryPageQuery
            {
                Keyword = "unique jacket",
                PageNumber = 1,
                PageSize = 20
            });
            var updated = await repository.QueryPageAsync(new QueryPageQuery
            {
                Keyword = "renamed coat",
                PageNumber = 1,
                PageSize = 20
            });

            Assert.Empty(stale.Items);
            Assert.Equal("ITEM-SEARCH", Assert.Single(updated.Items).InvoiceNo);
        }

        private static Invoice CreateInvoice(string invoiceNo, int day) => new()
        {
            InvoiceNo = invoiceNo,
            Type = "实际数据",
            InvoiceDate = new DateOnly(2026, 4, day),
            ShipmentDate = new DateOnly(2026, 4, day)
        };

        private static DatabaseConnectionSettings CreatePostgreSqlModeSettings()
        {
            return new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "127.0.0.1",
                PostgreSqlDatabase = "exportdoc_test",
                PostgreSqlUsername = "test_user"
            };
        }

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User currentUser)
            {
                CurrentUser = currentUser;
            }

            public User CurrentUser { get; }
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options;

            public TestDbContextFactory()
            {
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
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
            }
        }

        private sealed class SqliteTestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly SqliteConnection _connection = new("Data Source=:memory:");
            private readonly DbContextOptions<AppDbContext> _options;

            public SqliteTestDbContextFactory()
            {
                _connection.Open();
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(_connection)
                    .Options;
                using var context = CreateDbContext();
                DatabaseSchemaBaseline.EnsureCurrentAsync(context, usesPostgreSql: false)
                    .GetAwaiter()
                    .GetResult();
            }

            public AppDbContext CreateDbContext() => new(_options);

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(CreateDbContext());

            public void Dispose() => _connection.Dispose();
        }
    }
}

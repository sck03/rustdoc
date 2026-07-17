using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
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
                    new Invoice { InvoiceNo = "OWN-INV", Type = "实际数据", OwnerUserId = 7, InvoiceDate = new DateTime(2026, 4, 1), ShipmentDate = new DateTime(2026, 4, 1) },
                    new Invoice { InvoiceNo = "OTHER-INV", Type = "实际数据", OwnerUserId = 8, InvoiceDate = new DateTime(2026, 4, 2), ShipmentDate = new DateTime(2026, 4, 2) });
                context.Payments.AddRange(
                    new Payment { InvoiceNo = "OWN-PAY", OwnerUserId = 7, PaymentDate = new DateTime(2026, 4, 1) },
                    new Payment { InvoiceNo = "OTHER-PAY", OwnerUserId = 8, PaymentDate = new DateTime(2026, 4, 2) });
                await context.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var accessScope = new BusinessDataAccessScope(
                settings,
                new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" }));
            var repository = new LocalSharedReadRepository(factory, settings, accessScope);
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
            Assert.Equal("OWN-PAY", ownPaymentDetail.InvoiceNo);
            Assert.Null(foreignPaymentDetail);
        }

        [Fact]
        public async Task LocalSharedReadRepository_WhenPostgreSqlAdmin_ShouldReturnAllRows()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Invoices.AddRange(
                    new Invoice { InvoiceNo = "ADMIN-OWN", Type = "实际数据", OwnerUserId = 1, InvoiceDate = new DateTime(2026, 4, 1), ShipmentDate = new DateTime(2026, 4, 1) },
                    new Invoice { InvoiceNo = "ADMIN-OTHER", Type = "实际数据", OwnerUserId = 2, InvoiceDate = new DateTime(2026, 4, 2), ShipmentDate = new DateTime(2026, 4, 2) });
                await context.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var accessScope = new BusinessDataAccessScope(
                settings,
                new FixedCurrentUserContext(new User { Id = 1, Username = "admin", Role = "Admin" }));
            var repository = new LocalSharedReadRepository(factory, settings, accessScope);

            var invoices = await repository.QueryPageAsync(new InvoiceListPageQuery { PageNumber = 1, PageSize = 10 });

            Assert.Equal(2, invoices.TotalCount);
            Assert.Equal(["ADMIN-OTHER", "ADMIN-OWN"], invoices.Items.Select(invoice => invoice.InvoiceNo).ToArray());
        }

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
    }
}

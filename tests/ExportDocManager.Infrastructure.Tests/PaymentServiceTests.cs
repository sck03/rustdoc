using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class PaymentServiceTests
    {
        [Fact]
        public async Task SavePaymentAsync_ShouldApplyCurrentUserOwnership()
        {
            using var factory = new TestDbContextFactory();
            var settings = new DatabaseConnectionSettings();
            var service = new PaymentService(
                factory,
                settings,
                new BusinessDataAccessScope(
                    settings,
                    new FixedCurrentUserContext(new User
                    {
                        Id = 9,
                        Username = "creator",
                        Role = "User",
                        DepartmentId = "Doc",
                        CompanyScope = "CN"
                    })));

            await service.SavePaymentAsync(new Payment
            {
                InvoiceNo = " OWN-PAY ",
                PaymentDate = new DateTime(2026, 6, 22)
            });

            using var context = factory.CreateDbContext();
            var payment = await context.Payments.SingleAsync();
            Assert.Equal("OWN-PAY", payment.InvoiceNo);
            Assert.Equal(9, payment.OwnerUserId);
            Assert.Equal("Doc", payment.DepartmentId);
            Assert.Equal("CN", payment.CompanyScope);
        }

        [Fact]
        public async Task PaymentService_WhenPostgreSqlRegularUser_ShouldBlockForeignRows()
        {
            using var factory = new TestDbContextFactory();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.Payments.AddRange(
                    new Payment { InvoiceNo = "OWN-PAY", OwnerUserId = 7, PaymentDate = new DateTime(2026, 6, 22) },
                    new Payment { InvoiceNo = "FOREIGN-PAY", OwnerUserId = 8, PaymentDate = new DateTime(2026, 6, 22) });
                await seedContext.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var service = new PaymentService(
                factory,
                settings,
                new BusinessDataAccessScope(
                    settings,
                    new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" })));
            using var readContext = factory.CreateDbContext();
            var foreignPaymentId = await readContext.Payments
                .Where(payment => payment.InvoiceNo == "FOREIGN-PAY")
                .Select(payment => payment.Id)
                .SingleAsync();

            var deleted = await service.DeletePaymentAsync(foreignPaymentId);
            var updateException = await Assert.ThrowsAsync<Exception>(() =>
                service.SavePaymentAsync(new Payment
                {
                    Id = foreignPaymentId,
                    InvoiceNo = "FOREIGN-EDIT",
                    OwnerUserId = 8,
                    PaymentDate = new DateTime(2026, 6, 22)
                }));

            Assert.False(deleted);
            Assert.Contains("无权限", updateException.ToString());
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

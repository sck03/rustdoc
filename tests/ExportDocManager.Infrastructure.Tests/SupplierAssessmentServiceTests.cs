using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Suppliers;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SupplierAssessmentServiceTests
    {
        [Fact]
        public async Task Overview_WhenPostgreSqlRegularUser_ShouldOnlyAggregateOwnedSuppliers()
        {
            using var factory = new TestDbContextFactory();
            using (var context = factory.CreateDbContext())
            {
                var owned = new SupplierCompany
                {
                    OwnerUserId = 7,
                    CompanyScope = "acme",
                    Name = "Owned Supplier",
                    Status = "合作中"
                };
                var other = new SupplierCompany
                {
                    OwnerUserId = 8,
                    CompanyScope = "other",
                    Name = "Other Supplier",
                    Status = "合作中"
                };
                context.SupplierCompanies.AddRange(owned, other);
                await context.SaveChangesAsync();
                context.SupplierAssessments.AddRange(
                    new SupplierAssessment
                    {
                        SupplierCompanyId = owned.Id,
                        AssessmentDate = new DateOnly(2026, 8, 14),
                        AssessmentKind = "订单复盘",
                        QualityScore = 4,
                        DeliveryScore = 3,
                        ServiceScore = 5,
                        PriceScore = 4,
                        Conclusion = "合格",
                        Status = SupplierAssessmentStatusCatalog.Confirmed,
                        ConfirmedBy = "manager",
                        ConfirmedAt = DateTimeOffset.UtcNow
                    },
                    new SupplierAssessment
                    {
                        SupplierCompanyId = other.Id,
                        AssessmentDate = new DateOnly(2026, 8, 15),
                        AssessmentKind = "订单复盘",
                        QualityScore = 1,
                        DeliveryScore = 1,
                        ServiceScore = 1,
                        PriceScore = 1,
                        Conclusion = "暂停合作",
                        Status = SupplierAssessmentStatusCatalog.Confirmed,
                        ConfirmedBy = "manager",
                        ConfirmedAt = DateTimeOffset.UtcNow
                    });
                await context.SaveChangesAsync();
            }

            var currentUser = new FixedCurrentUserContext(new User
            {
                Id = 7,
                Username = "sales",
                Role = "Sales",
                CompanyScope = "acme"
            });
            var service = new SupplierAssessmentService(factory,
                new BusinessDataAccessScope(CreatePostgreSqlModeSettings(), currentUser),
                BusinessClock.CreateSystem());

            var overview = await service.GetOverviewAsync();

            Assert.Equal(1, overview.TotalSuppliers);
            Assert.Equal(1, overview.AssessedSuppliers);
            Assert.Equal(0, overview.PausedCount);
            var item = Assert.Single(overview.Items);
            Assert.Equal("Owned Supplier", item.SupplierName);
            Assert.Equal(4m, item.AverageScore);
        }

        private static DatabaseConnectionSettings CreatePostgreSqlModeSettings() => new()
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "127.0.0.1",
            PostgreSqlDatabase = "exportdoc_test",
            PostgreSqlUsername = "test_user"
        };

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User currentUser) => CurrentUser = currentUser;
            public User CurrentUser { get; }
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            public AppDbContext CreateDbContext() => new(_options);
            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(CreateDbContext());
            public void Dispose()
            {
                using var context = CreateDbContext();
                context.Database.EnsureDeleted();
            }
        }
    }
}

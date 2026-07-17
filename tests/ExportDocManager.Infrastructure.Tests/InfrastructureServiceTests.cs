using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class InfrastructureServiceTests
    {
        [Fact]
        public void CustomOptionService_ShouldReturnTrimmedDistinctValues()
        {
            using var factory = new TestDbContextFactory();
            using (var context = factory.CreateDbContext())
            {
                context.CustomOptions.AddRange(
                    new CustomOption { OptionType = "PaymentMethod", OptionValue = "  TT  ", CreatedDate = new DateTime(2026, 4, 1, 8, 0, 0) },
                    new CustomOption { OptionType = "PaymentMethod", OptionValue = "tt", CreatedDate = new DateTime(2026, 4, 1, 9, 0, 0) },
                    new CustomOption { OptionType = "PaymentMethod", OptionValue = " OA ", CreatedDate = new DateTime(2026, 4, 1, 10, 0, 0) });
                context.SaveChanges();
            }

            var service = new CustomOptionService(factory);

            Assert.Equal(["TT", "OA"], service.GetOptions(" PaymentMethod "));
        }

        [Fact]
        public void SharedDatabaseCapabilityService_ShouldReportPendingPostgreSqlConfiguration()
        {
            var service = new SharedDatabaseCapabilityService(new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "127.0.0.1"
            });

            var profile = service.GetCurrentProfile();

            Assert.False(profile.SharedDatabaseEnabled);
            Assert.True(profile.SharedDatabasePendingConfiguration);
            Assert.Contains("尚未配置完成", profile.CurrentModeText);
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

            public void Dispose()
            {
                using var context = CreateDbContext();
                context.Database.EnsureDeleted();
            }
        }
    }
}

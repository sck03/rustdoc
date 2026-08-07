using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class InfrastructureServiceTests
    {
        [Fact]
        public async Task CustomOptionService_ShouldReturnTrimmedDistinctValues()
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

            Assert.Equal(["TT", "OA"], await service.GetOptionsAsync(" PaymentMethod "));
        }

        [Fact]
        public async Task CustomOptionService_ShouldBoundReturnedHistoryToLatestFiveHundredValues()
        {
            using var factory = new TestDbContextFactory();
            using (var context = factory.CreateDbContext())
            {
                var createdAt = new DateTime(2026, 4, 1, 8, 0, 0);
                context.CustomOptions.AddRange(Enumerable.Range(0, 505).Select(index => new CustomOption
                {
                    OptionType = "PortOfLoading",
                    OptionValue = $"Value-{index:D3}",
                    CreatedDate = createdAt.AddMinutes(index)
                }));
                context.SaveChanges();
            }

            var service = new CustomOptionService(factory);
            var options = await service.GetOptionsAsync("PortOfLoading");

            Assert.Equal(500, options.Count);
            Assert.Equal("Value-005", options[0]);
            Assert.Equal("Value-504", options[^1]);
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

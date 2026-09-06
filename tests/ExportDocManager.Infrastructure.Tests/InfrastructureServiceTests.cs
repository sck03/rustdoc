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
            using var factory = new InMemoryTestDatabase();
            using (var context = factory.CreateDbContext())
            {
                context.CustomOptions.AddRange(
                    new CustomOption { OptionType = "PaymentMethod", OptionValue = "  TT  ", CreatedAt = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero) },
                    new CustomOption { OptionType = "PaymentMethod", OptionValue = "tt", CreatedAt = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero) },
                    new CustomOption { OptionType = "PaymentMethod", OptionValue = " OA ", CreatedAt = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero) });
                context.SaveChanges();
            }

            var service = new CustomOptionService(factory);

            Assert.Equal(["TT", "OA"], await service.GetOptionsAsync(" PaymentMethod "));
        }

        [Fact]
        public async Task CustomOptionService_ShouldBoundReturnedHistoryToLatestFiveHundredValues()
        {
            using var factory = new InMemoryTestDatabase();
            using (var context = factory.CreateDbContext())
            {
                var createdAt = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);
                context.CustomOptions.AddRange(Enumerable.Range(0, 505).Select(index => new CustomOption
                {
                    OptionType = "PortOfLoading",
                    OptionValue = $"Value-{index:D3}",
                    CreatedAt = createdAt.AddMinutes(index)
                }));
                context.SaveChanges();
            }

            var service = new CustomOptionService(factory);
            var options = await service.GetOptionsAsync("PortOfLoading");

            Assert.Equal(500, options.Count);
            Assert.Equal("Value-005", options[0]);
            Assert.Equal("Value-504", options[^1]);
        }

    }
}

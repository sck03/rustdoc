using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class MasterDataReadRepositoryTests
    {
        [Fact]
        public async Task QueryAsync_ShouldSearchCustomersAcrossContactFields()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Customers.AddRange(
                    new Customer
                    {
                        CustomerNameEN = "Alpha Trading",
                        Email = "alpha@example.com",
                        TaxId = "91310000ALPHA"
                    },
                    new Customer
                    {
                        CustomerNameEN = "Beta Trading",
                        Email = "beta@example.com",
                        TaxId = "91310000BETA"
                    });
                await context.SaveChangesAsync();
            }

            ICustomerReadRepository repository = new LocalMasterDataReadRepository(factory);

            var result = await repository.QueryAsync(new CustomerReadQuery { Keyword = "BETA" });

            var matched = Assert.Single(result);
            Assert.Equal("Beta Trading", matched.CustomerNameEN);
        }

        [Fact]
        public async Task QueryPageAsync_ShouldMatchFormattedHsCodeByNormalizedCode()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.HsCodes.AddRange(
                    new HsCode { Code = "6109100010", Name = "棉制男式T恤衫" },
                    new HsCode { Code = "6205200000", Name = "棉制衬衫" });
                await context.SaveChangesAsync();
            }

            IHsCodeReadRepository repository = new LocalMasterDataReadRepository(factory);

            var result = await repository.QueryPageAsync(new HsCodeReadQuery
            {
                Keyword = "6109.1000-10",
                PageNumber = 1,
                PageSize = 10
            });

            var matched = Assert.Single(result.Items);
            Assert.Equal("6109100010", matched.Code);
            Assert.Equal("6109100010", matched.NormalizedCode);
            Assert.Equal(1, result.TotalCount);
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

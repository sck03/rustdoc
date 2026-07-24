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

        [Fact]
        public async Task HsCodeQueryPageAsync_ShouldClampInternalPageSize()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.HsCodes.AddRange(Enumerable.Range(1, 205).Select(index => new HsCode
                {
                    Code = $"{index:0000000000}",
                    Name = $"测试编码 {index}"
                }));
                await context.SaveChangesAsync();
            }

            IHsCodeReadRepository repository = new LocalMasterDataReadRepository(factory);
            var result = await repository.QueryPageAsync(new HsCodeReadQuery
            {
                PageNumber = 1,
                PageSize = int.MaxValue
            });

            Assert.Equal(200, result.PageSize);
            Assert.Equal(200, result.Items.Count);
            Assert.Equal(205, result.TotalCount);
        }

        [Fact]
        public async Task ProductQueryPageAsync_ShouldPageAndFilterInDatabaseOrder()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                for (int index = 1; index <= 25; index++)
                {
                    context.Products.Add(new Product
                    {
                        ProductCode = $"SKU-{index:00}",
                        NameEN = index % 2 == 0 ? $"COTTON SHIRT {index:00}" : $"POLYESTER SHIRT {index:00}",
                        NameCN = "衬衫"
                    });
                }
                await context.SaveChangesAsync();
            }

            IProductReadRepository repository = new LocalMasterDataReadRepository(factory);
            var result = await repository.QueryPageAsync(new ProductReadQuery
            {
                Keyword = "COTTON",
                PageNumber = 2,
                PageSize = 5
            });

            Assert.Equal(12, result.TotalCount);
            Assert.Equal(2, result.PageNumber);
            Assert.Equal(5, result.Items.Count);
            Assert.Equal("SKU-12", result.Items[0].ProductCode);
            Assert.All(result.Items, item => Assert.Contains("COTTON", item.NameEN, StringComparison.Ordinal));
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

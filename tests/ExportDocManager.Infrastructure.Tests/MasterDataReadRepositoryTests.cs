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

            ICustomerReadRepository repository = CreateRepository(factory);

            var result = await repository.QueryAsync(new CustomerReadQuery { Keyword = "BETA" });

            var matched = Assert.Single(result);
            Assert.Equal("Beta Trading", matched.CustomerNameEN);
        }

        [Fact]
        public async Task SharedMasterDataQueryPageAsync_ShouldReturnStableDatabasePages()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                foreach (int index in Enumerable.Range(1, 25))
                {
                    context.Customers.Add(new Customer { CustomerNameEN = $"Customer {index:00}" });
                    context.Exporters.Add(new Exporter { ExporterNameEN = $"Exporter {index:00}" });
                    context.Payees.Add(new Payee { Category = "Supplier", Name = $"Payee {index:00}" });
                    context.Ports.Add(new Port { NameEN = $"Port {index:00}" });
                    context.Units.Add(new Unit { NameEN = $"Unit {index:00}" });
                }

                await context.SaveChangesAsync();
            }

            var repository = CreateRepository(factory);
            var customers = await ((ICustomerReadRepository)repository).QueryPageAsync(
                new CustomerReadQuery { PageNumber = 2, PageSize = 10 });
            var exporters = await ((IExporterReadRepository)repository).QueryPageAsync(
                new ExporterReadQuery { PageNumber = 2, PageSize = 10 });
            var payees = await ((IPayeeReadRepository)repository).QueryPageAsync(
                new PayeeReadQuery { PageNumber = 2, PageSize = 10 });
            var ports = await ((IPortReadRepository)repository).QueryPageAsync(
                new PortReadQuery { PageNumber = 2, PageSize = 10 });
            var units = await ((IUnitReadRepository)repository).QueryPageAsync(
                new UnitReadQuery { PageNumber = 2, PageSize = 10 });

            Assert.Equal((25, 2, 10, "Customer 11"),
                (customers.TotalCount, customers.PageNumber, customers.Items.Count, customers.Items[0].CustomerNameEN));
            Assert.Equal((25, 2, 10, "Exporter 11"),
                (exporters.TotalCount, exporters.PageNumber, exporters.Items.Count, exporters.Items[0].ExporterNameEN));
            Assert.Equal((25, 2, 10, "Payee 11"),
                (payees.TotalCount, payees.PageNumber, payees.Items.Count, payees.Items[0].Name));
            Assert.Equal((25, 2, 10, "Port 11"),
                (ports.TotalCount, ports.PageNumber, ports.Items.Count, ports.Items[0].NameEN));
            Assert.Equal((25, 2, 10, "Unit 11"),
                (units.TotalCount, units.PageNumber, units.Items.Count, units.Items[0].NameEN));
        }

        [Fact]
        public async Task GetByIdAsync_ShouldFindRecordsBeyondDefaultListLimit()
        {
            using var factory = new TestDbContextFactory();
            int payeeId;
            int portId;
            int unitId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                for (int index = 1; index <= 205; index++)
                {
                    context.Payees.Add(new Payee { Category = "Supplier", Name = $"Payee {index:000}" });
                    context.Ports.Add(new Port { NameEN = $"Port {index:000}" });
                    context.Units.Add(new Unit { NameEN = $"Unit {index:000}" });
                }

                await context.SaveChangesAsync();
                payeeId = await context.Payees.OrderBy(item => item.Id).Select(item => item.Id).LastAsync();
                portId = await context.Ports.OrderBy(item => item.Id).Select(item => item.Id).LastAsync();
                unitId = await context.Units.OrderBy(item => item.Id).Select(item => item.Id).LastAsync();
            }

            var repository = CreateRepository(factory);

            Assert.Equal("Payee 205", (await ((IPayeeReadRepository)repository).GetByIdAsync(payeeId))?.Name);
            Assert.Equal("Port 205", (await ((IPortReadRepository)repository).GetByIdAsync(portId))?.NameEN);
            Assert.Equal("Unit 205", (await ((IUnitReadRepository)repository).GetByIdAsync(unitId))?.NameEN);
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

            IHsCodeReadRepository repository = CreateRepository(factory);

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

            IHsCodeReadRepository repository = CreateRepository(factory);
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

            IProductReadRepository repository = CreateRepository(factory);
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

        private static LocalMasterDataReadRepository CreateRepository(IDbContextFactory<AppDbContext> factory) =>
            new(factory, TestAccessScope.Create());

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

using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.Infrastructure.Tests
{
    public class MasterDataServiceTests
    {
        [Fact]
        public async Task CustomerService_ShouldNormalizeAndReturnSavedCustomer()
        {
            using var factory = new TestDbContextFactory();
            var service = new CustomerService(factory, new LocalMasterDataReadRepository(factory));

            await service.SaveCustomerAsync(new Customer
            {
                CustomerNameEN = "  Alpha Trading  ",
                NotifyPartyName = "  Alpha Notify  ",
                TaxId = "  91310000ALPHA  "
            });

            var customer = await service.GetCustomerByNameAsync(" Alpha Trading ");

            Assert.NotNull(customer);
            Assert.Equal("Alpha Trading", customer.CustomerNameEN);
            Assert.Equal("Alpha Notify", customer.NotifyPartyName);
            Assert.Equal("91310000ALPHA", customer.TaxId);
        }

        [Fact]
        public async Task ProductService_ShouldNormalizeCodeHsCodeAndUnits()
        {
            using var factory = new TestDbContextFactory();
            var service = new ProductService(factory, new LocalMasterDataReadRepository(factory));

            await service.AddProductAsync(new Product
            {
                ProductCode = "  sku-001  ",
                NameEN = "  Cotton Shirt  ",
                HSCode = "  62052000  ",
                UnitEN = "  pcs  ",
                PackageUnitEN = "  ctn  "
            });

            var product = await service.GetByCodeAsync("sku-001");

            Assert.NotNull(product);
            Assert.Equal("sku-001", product.ProductCode);
            Assert.Equal("Cotton Shirt", product.NameEN);
            Assert.Equal("62052000", product.HSCode);
            Assert.Equal("PCS", product.UnitEN);
            Assert.Equal("CTN", product.PackageUnitEN);
        }

        [Fact]
        public async Task AuxiliaryService_ShouldNormalizePortsAndUnits()
        {
            using var factory = new TestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new AuxiliaryService(factory, repository, repository);

            await service.SavePortAsync(new Port
            {
                Code = "  cnnbo  ",
                NameEN = "  Ningbo  ",
                NameCN = "  宁波  ",
                Country = "  China  "
            });
            await service.SaveUnitAsync(new Unit
            {
                Code = "  pcs  ",
                NameEN = "  Piece  ",
                NameCN = "  件  "
            });

            var port = Assert.Single(await service.SearchPortsAsync("CNNBO"));
            var unit = Assert.Single(await service.SearchUnitsAsync("PCS"));

            Assert.Equal("CNNBO", port.Code);
            Assert.Equal("Ningbo", port.NameEN);
            Assert.Equal("宁波", port.NameCN);
            Assert.Equal("China", port.Country);
            Assert.Equal("PCS", unit.Code);
            Assert.Equal("Piece", unit.NameEN);
            Assert.Equal("件", unit.NameCN);
        }

        [Fact]
        public async Task MasterDataDeleteServices_ShouldRemoveExistingRowsWithRowVersion()
        {
            using var factory = new SqliteTestDbContextFactory(new AuditInterceptor());
            var repository = new LocalMasterDataReadRepository(factory);
            var customerService = new CustomerService(factory, repository);
            var exporterService = new ExporterService(factory, repository);
            var payeeService = new PayeeService(factory, repository);

            int customerId = await customerService.SaveCustomerAsync(new Customer
            {
                CustomerNameEN = "Delete Buyer"
            });
            int exporterId = await exporterService.SaveExporterAsync(new Exporter
            {
                ExporterNameEN = "Delete Exporter"
            });
            int payeeId = await payeeService.SavePayeeAsync(new Payee
            {
                Category = "Factory",
                Name = "Delete Payee"
            });

            Assert.True(await customerService.DeleteCustomerAsync(customerId));
            Assert.True(await exporterService.DeleteExporterAsync(exporterId));
            Assert.True(await payeeService.DeletePayeeAsync(payeeId));
            Assert.False(await customerService.DeleteCustomerAsync(customerId));
            Assert.False(await exporterService.DeleteExporterAsync(exporterId));
            Assert.False(await payeeService.DeletePayeeAsync(payeeId));

            await using var verifyContext = await factory.CreateDbContextAsync();
            Assert.Empty(await verifyContext.Customers.ToListAsync());
            Assert.Empty(await verifyContext.Exporters.ToListAsync());
            Assert.Empty(await verifyContext.Payees.ToListAsync());
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
            private readonly SqliteConnection _connection;
            private readonly DbContextOptions<AppDbContext> _options;

            public SqliteTestDbContextFactory(params IInterceptor[] interceptors)
            {
                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();

                var builder = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(_connection);

                if (interceptors != null && interceptors.Length > 0)
                {
                    builder.AddInterceptors(interceptors);
                }

                _options = builder.Options;

                using var context = CreateDbContext();
                context.Database.EnsureCreated();
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
                _connection.Dispose();
                SqliteConnection.ClearAllPools();
            }
        }
    }
}

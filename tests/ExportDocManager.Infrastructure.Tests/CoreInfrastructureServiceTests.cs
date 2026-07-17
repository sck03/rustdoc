using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.Infrastructure.Tests
{
    public class CoreInfrastructureServiceTests
    {
        [Fact]
        public async Task ItemService_SaveItemsAsync_ShouldNormalizeAndRemoveMissingRows()
        {
            using var factory = new TestDbContextFactory();
            int invoiceId;
            int existingItemId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                var invoice = new Invoice
                {
                    InvoiceNo = "INV-ITEM",
                    InvoiceDate = new DateTime(2026, 6, 22),
                    ShipmentDate = new DateTime(2026, 6, 22)
                };
                context.Invoices.Add(invoice);
                await context.SaveChangesAsync();
                invoiceId = invoice.Id;

                var existing = new Item { InvoiceId = invoiceId, StyleNo = "OLD" };
                var removed = new Item { InvoiceId = invoiceId, StyleNo = "REMOVE" };
                context.Items.AddRange(existing, removed);
                await context.SaveChangesAsync();
                existingItemId = existing.Id;
            }

            var service = new ItemService(factory);

            await service.SaveItemsAsync(invoiceId, new List<Item>
            {
                new() { Id = existingItemId, StyleNo = "  ST-001  ", StyleName = "  Shirt  ", HSCode = "  62052000  " },
                new() { StyleNo = "  ST-002  ", StyleName = "  Pants  " }
            });

            await using var verifyContext = await factory.CreateDbContextAsync();
            var items = await verifyContext.Items
                .OrderBy(item => item.StyleNo)
                .ToListAsync();

            Assert.Equal(2, items.Count);
            Assert.DoesNotContain(items, item => item.StyleNo == "REMOVE");
            Assert.Contains(items, item => item.Id == existingItemId && item.StyleNo == "ST-001" && item.StyleName == "Shirt" && item.HSCode == "62052000");
            Assert.Contains(items, item => item.Id != existingItemId && item.StyleNo == "ST-002" && item.StyleName == "Pants");
        }

        [Fact]
        public async Task InvoiceService_SaveInvoiceAsync_ShouldRemoveExistingItemsWhenItemsAreEmpty()
        {
            using var factory = new TestDbContextFactory();
            int invoiceId;
            await using (var context = await factory.CreateDbContextAsync())
            {
                var invoice = new Invoice
                {
                    InvoiceNo = "INV-CLEAR-ITEMS",
                    InvoiceDate = new DateTime(2026, 6, 23),
                    ShipmentDate = new DateTime(2026, 6, 23),
                    Items =
                    [
                        new Item { StyleNo = "OLD-1", StyleName = "Old Item 1" },
                        new Item { StyleNo = "OLD-2", StyleName = "Old Item 2" }
                    ]
                };
                context.Invoices.Add(invoice);
                await context.SaveChangesAsync();
                invoiceId = invoice.Id;
            }

            var service = new InvoiceService(
                factory,
                new ItemService(factory),
                new InvoicePartyResolver(),
                new DatabaseConnectionSettings());

            await service.SaveInvoiceAsync(new Invoice
            {
                Id = invoiceId,
                InvoiceNo = "INV-CLEAR-ITEMS",
                InvoiceDate = new DateTime(2026, 6, 23),
                ShipmentDate = new DateTime(2026, 6, 23),
                Items = []
            });

            await using var verifyContext = await factory.CreateDbContextAsync();
            Assert.Empty(await verifyContext.Items.Where(item => item.InvoiceId == invoiceId).ToListAsync());
        }

        [Fact]
        public async Task InvoicePartyResolver_ShouldReuseExistingCustomerByNormalizedName()
        {
            using var factory = new TestDbContextFactory();
            await using (var context = await factory.CreateDbContextAsync())
            {
                context.Customers.Add(new Customer
                {
                    CustomerNameEN = "Alpha Trading",
                    TaxId = "91310000ALPHA"
                });
                await context.SaveChangesAsync();
            }

            var resolver = new InvoicePartyResolver();
            await using var resolveContext = await factory.CreateDbContextAsync();

            var customerId = await resolver.ResolveCustomerIdAsync(
                resolveContext,
                new Customer { CustomerNameEN = "  Alpha Trading  " });

            Assert.True(customerId > 0);
            Assert.Equal(1, await resolveContext.Customers.CountAsync());
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options;

            public TestDbContextFactory()
            {
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

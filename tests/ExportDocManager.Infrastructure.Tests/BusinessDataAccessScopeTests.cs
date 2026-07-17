using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class BusinessDataAccessScopeTests
    {
        [Fact]
        public async Task ApplyInvoiceScope_WhenPostgreSqlRegularUser_ShouldFilterOwnedRows()
        {
            using var factory = new TestDbContextFactory();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.Invoices.AddRange(
                    new Invoice
                    {
                        InvoiceNo = "OWN-INV",
                        Type = "实际数据",
                        OwnerUserId = 7,
                        InvoiceDate = new DateTime(2026, 6, 22),
                        ShipmentDate = new DateTime(2026, 6, 22)
                    },
                    new Invoice
                    {
                        InvoiceNo = "FOREIGN-INV",
                        Type = "实际数据",
                        OwnerUserId = 8,
                        InvoiceDate = new DateTime(2026, 6, 22),
                        ShipmentDate = new DateTime(2026, 6, 22)
                    });
                await seedContext.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var scope = new BusinessDataAccessScope(
                settings,
                new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" }));

            using var context = factory.CreateDbContext();
            var invoices = await scope.ApplyInvoiceScope(context.Invoices.AsNoTracking())
                .OrderBy(invoice => invoice.InvoiceNo)
                .ToListAsync();
            var canAccessForeign = await scope.CanAccessInvoiceAsync(
                context,
                await context.Invoices
                    .Where(invoice => invoice.InvoiceNo == "FOREIGN-INV")
                    .Select(invoice => invoice.Id)
                    .SingleAsync());

            var invoice = Assert.Single(invoices);
            Assert.Equal("OWN-INV", invoice.InvoiceNo);
            Assert.False(canAccessForeign);
        }

        [Fact]
        public void ApplyOwner_ShouldAssignInvoiceOwnershipFromCurrentUser()
        {
            var scope = new BusinessDataAccessScope(
                new DatabaseConnectionSettings(),
                new FixedCurrentUserContext(new User
                {
                    Id = 9,
                    Username = "creator",
                    Role = "User",
                    DepartmentId = "Doc",
                    CompanyScope = "CN"
                }));
            var invoice = new Invoice { InvoiceNo = "NEW-INV" };

            scope.ApplyOwner(invoice);

            Assert.Equal(9, invoice.OwnerUserId);
            Assert.Equal("Doc", invoice.DepartmentId);
            Assert.Equal("CN", invoice.CompanyScope);
        }

        [Fact]
        public async Task EmailTemplateScopes_WhenPostgreSqlRegularUser_ShouldReadSharedButEditOwnedOnly()
        {
            using var factory = new TestDbContextFactory();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.EmailTemplates.AddRange(
                    new EmailTemplate { OwnerUserId = 7, Name = "我的模板", IsShared = false },
                    new EmailTemplate { OwnerUserId = 8, Name = "团队模板", IsShared = true },
                    new EmailTemplate { OwnerUserId = 8, Name = "他人私有模板", IsShared = false });
                await seedContext.SaveChangesAsync();
            }

            var scope = new BusinessDataAccessScope(
                CreatePostgreSqlModeSettings(),
                new FixedCurrentUserContext(new User { Id = 7, Username = "sales", Role = "Sales" }));
            using var context = factory.CreateDbContext();

            var readable = await scope.ApplyEmailTemplateScope(context.EmailTemplates.AsNoTracking())
                .OrderBy(item => item.Name).Select(item => item.Name).ToListAsync();
            var editable = await scope.ApplyOwnedEmailTemplateScope(context.EmailTemplates.AsNoTracking())
                .Select(item => item.Name).ToListAsync();

            Assert.Equal(new[] { "团队模板", "我的模板" }, readable);
            Assert.Equal("我的模板", Assert.Single(editable));
        }

        [Fact]
        public async Task SingleWindowScopes_WhenPostgreSqlRegularUser_ShouldFilterBySourceInvoiceOwner()
        {
            using var factory = new TestDbContextFactory();
            using (var seedContext = factory.CreateDbContext())
            {
                var ownInvoice = new Invoice
                {
                    InvoiceNo = "OWN-SW",
                    Type = "实际数据",
                    OwnerUserId = 7,
                    InvoiceDate = new DateTime(2026, 6, 22),
                    ShipmentDate = new DateTime(2026, 6, 22)
                };
                var foreignInvoice = new Invoice
                {
                    InvoiceNo = "FOREIGN-SW",
                    Type = "实际数据",
                    OwnerUserId = 8,
                    InvoiceDate = new DateTime(2026, 6, 22),
                    ShipmentDate = new DateTime(2026, 6, 22)
                };
                seedContext.Invoices.AddRange(ownInvoice, foreignInvoice);
                await seedContext.SaveChangesAsync();

                seedContext.SwSubmissionBatches.AddRange(
                    new SwSubmissionBatch
                    {
                        BatchReference = "OWN-BATCH",
                        BusinessType = SingleWindowBusinessType.CustomsCoo.ToString(),
                        SourceInvoiceId = ownInvoice.Id,
                        InvoiceNo = ownInvoice.InvoiceNo
                    },
                    new SwSubmissionBatch
                    {
                        BatchReference = "FOREIGN-BATCH",
                        BusinessType = SingleWindowBusinessType.CustomsCoo.ToString(),
                        SourceInvoiceId = foreignInvoice.Id,
                        InvoiceNo = foreignInvoice.InvoiceNo
                    });
                seedContext.SwOperationTickets.AddRange(
                    new SwOperationTicket
                    {
                        BusinessType = "海关原产地证",
                        SourceInvoiceId = ownInvoice.Id,
                        Status = SingleWindowCollaborationStatusCatalog.Pending
                    },
                    new SwOperationTicket
                    {
                        BusinessType = "海关原产地证",
                        SourceInvoiceId = foreignInvoice.Id,
                        Status = SingleWindowCollaborationStatusCatalog.Pending
                    });
                await seedContext.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var scope = new BusinessDataAccessScope(
                settings,
                new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" }));

            using var context = factory.CreateDbContext();
            var batches = await scope.ApplySubmissionBatchScope(context.SwSubmissionBatches.AsNoTracking(), context)
                .OrderBy(batch => batch.BatchReference)
                .ToListAsync();
            var tickets = await scope.ApplyOperationTicketScope(context.SwOperationTickets.AsNoTracking(), context)
                .OrderBy(ticket => ticket.SourceInvoiceId)
                .ToListAsync();

            var batch = Assert.Single(batches);
            var ticket = Assert.Single(tickets);
            Assert.Equal("OWN-BATCH", batch.BatchReference);
            Assert.Equal("OWN-SW", batch.InvoiceNo);
            Assert.Equal(context.Invoices.Single(invoice => invoice.InvoiceNo == "OWN-SW").Id, ticket.SourceInvoiceId);
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

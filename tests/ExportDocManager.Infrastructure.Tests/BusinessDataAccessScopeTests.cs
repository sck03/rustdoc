using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class BusinessDataAccessScopeTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ApplyInvoiceScope_WhenExplicitPermissionsOmitInvoices_ShouldNotInheritRoleDefaults(
            bool hasAssignedTemplate)
        {
            using var factory = new InMemoryTestDatabase();
            using var context = factory.CreateDbContext();
            context.Invoices.Add(new Invoice { InvoiceNo = "OWN-RESTRICTED", OwnerUserId = 7 });
            await context.SaveChangesAsync();

            var user = new User { Id = 7, Username = "restricted", Role = "User" };
            if (hasAssignedTemplate)
            {
                user.PermissionTemplateId = 23;
            }
            else
            {
                user.EffectivePermissionGrants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [PermissionResourceCatalog.CreateGrantKey(
                        PermissionResourceCatalog.ReportTemplates, PermissionAction.Clone)] = PermissionDataScope.Own
                };
            }

            var scope = new BusinessDataAccessScope(
                CreatePostgreSqlModeSettings(), new FixedCurrentUserContext(user));

            Assert.Empty(await scope.ApplyInvoiceScope(context.Invoices.AsNoTracking()).ToListAsync());
            Assert.False(scope.HasPermission(PermissionResourceCatalog.ReportTemplates, PermissionAction.Design));
        }

        [Fact]
        public async Task ApplyInvoiceScope_WhenPostgreSqlRegularUser_ShouldFilterOwnedRows()
        {
            using var factory = new InMemoryTestDatabase();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.Invoices.AddRange(
                    new Invoice
                    {
                        InvoiceNo = "OWN-INV",
                        Type = "实际数据",
                        OwnerUserId = 7,
                        InvoiceDate = new DateOnly(2026, 6, 22),
                        ShipmentDate = new DateOnly(2026, 6, 22)
                    },
                    new Invoice
                    {
                        InvoiceNo = "FOREIGN-INV",
                        Type = "实际数据",
                        OwnerUserId = 8,
                        InvoiceDate = new DateOnly(2026, 6, 22),
                        ShipmentDate = new DateOnly(2026, 6, 22)
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
        public async Task CustomerAndExporterScopes_WhenPostgreSqlRegularUser_ShouldFilterOwnedRows()
        {
            using var factory = new InMemoryTestDatabase();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.Customers.AddRange(
                    new Customer { CustomerNameEN = "Own Customer", OwnerUserId = 7 },
                    new Customer { CustomerNameEN = "Foreign Customer", OwnerUserId = 8 });
                seedContext.Exporters.AddRange(
                    new Exporter { ExporterNameEN = "Own Exporter", OwnerUserId = 7 },
                    new Exporter { ExporterNameEN = "Foreign Exporter", OwnerUserId = 8 });
                seedContext.Payees.AddRange(
                    new Payee { Name = "Own Payee", OwnerUserId = 7 },
                    new Payee { Name = "Foreign Payee", OwnerUserId = 8 });
                await seedContext.SaveChangesAsync();
            }

            var scope = new BusinessDataAccessScope(
                CreatePostgreSqlModeSettings(),
                new FixedCurrentUserContext(new User { Id = 7, Username = "document-a", Role = "User" }));
            using var context = factory.CreateDbContext();

            var customer = Assert.Single(await scope
                .ApplyCustomerScope(context.Customers.AsNoTracking())
                .ToListAsync());
            var exporter = Assert.Single(await scope
                .ApplyExporterScope(context.Exporters.AsNoTracking())
                .ToListAsync());
            var payee = Assert.Single(await scope
                .ApplyPayeeScope(context.Payees.AsNoTracking())
                .ToListAsync());

            Assert.Equal("Own Customer", customer.CustomerNameEN);
            Assert.Equal("Own Exporter", exporter.ExporterNameEN);
            Assert.Equal("Own Payee", payee.Name);
        }

        [Fact]
        public void ApplyOwner_ShouldAssignCustomerAndExporterOwnershipFromCurrentUser()
        {
            var scope = new BusinessDataAccessScope(
                CreatePostgreSqlModeSettings(),
                new FixedCurrentUserContext(new User
                {
                    Id = 9,
                    Username = "document-owner",
                    Role = "User",
                    DepartmentId = "DOC",
                    CompanyScope = "CN"
                }));
            var customer = new Customer { CustomerNameEN = "Customer" };
            var exporter = new Exporter { ExporterNameEN = "Exporter" };
            var payee = new Payee { Name = "Payee" };

            scope.ApplyOwner(customer);
            scope.ApplyOwner(exporter);
            scope.ApplyOwner(payee);

            Assert.Equal(9, customer.OwnerUserId);
            Assert.Equal("DOC", customer.DepartmentId);
            Assert.Equal("CN", customer.CompanyScope);
            Assert.Equal(9, exporter.OwnerUserId);
            Assert.Equal("DOC", exporter.DepartmentId);
            Assert.Equal("CN", exporter.CompanyScope);
            Assert.Equal(9, payee.OwnerUserId);
            Assert.Equal("DOC", payee.DepartmentId);
            Assert.Equal("CN", payee.CompanyScope);
        }

        [Fact]
        public async Task EmailTemplateScopes_WhenPostgreSqlRegularUser_ShouldReadSharedButEditOwnedOnly()
        {
            using var factory = new InMemoryTestDatabase();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.EmailTemplates.AddRange(
                    new EmailTemplate
                    {
                        OwnerUserId = 7,
                        Name = "我的模板",
                        Status = TemplateLifecycleStatusCatalog.Draft,
                        ShareScope = TemplateShareScopeCatalog.Private
                    },
                    new EmailTemplate
                    {
                        OwnerUserId = 8,
                        Name = "团队模板",
                        Status = TemplateLifecycleStatusCatalog.Published,
                        ShareScope = TemplateShareScopeCatalog.All
                    },
                    new EmailTemplate
                    {
                        OwnerUserId = 8,
                        Name = "他人私有模板",
                        Status = TemplateLifecycleStatusCatalog.Published,
                        ShareScope = TemplateShareScopeCatalog.Private
                    });
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
            using var factory = new InMemoryTestDatabase();
            using (var seedContext = factory.CreateDbContext())
            {
                var ownInvoice = new Invoice
                {
                    InvoiceNo = "OWN-SW",
                    Type = "实际数据",
                    OwnerUserId = 7,
                    InvoiceDate = new DateOnly(2026, 6, 22),
                    ShipmentDate = new DateOnly(2026, 6, 22)
                };
                var foreignInvoice = new Invoice
                {
                    InvoiceNo = "FOREIGN-SW",
                    Type = "实际数据",
                    OwnerUserId = 8,
                    InvoiceDate = new DateOnly(2026, 6, 22),
                    ShipmentDate = new DateOnly(2026, 6, 22)
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
            var batch = Assert.Single(batches);
            Assert.Equal("OWN-BATCH", batch.BatchReference);
            Assert.Equal("OWN-SW", batch.InvoiceNo);
        }

        [Theory]
        [InlineData(PermissionDataScope.Department)]
        [InlineData(PermissionDataScope.Company)]
        [InlineData(PermissionDataScope.All)]
        [InlineData(PermissionDataScope.Own)]
        [InlineData("")]
        public async Task SharedTemplateScopes_ShouldRespectTheSharingAudienceAtEveryPermissionScope(string dataScope)
        {
            using var factory = new InMemoryTestDatabase();
            using var context = factory.CreateDbContext();
            var templates = new[]
            {
                ("own", 7, "C1", "D1", TemplateShareScopeCatalog.Private, TemplateLifecycleStatusCatalog.Draft),
                ("all", 8, "C2", "D2", TemplateShareScopeCatalog.All, TemplateLifecycleStatusCatalog.Published),
                ("company", 8, "C1", "D2", TemplateShareScopeCatalog.Company, TemplateLifecycleStatusCatalog.Published),
                ("department", 8, "C1", "D1", TemplateShareScopeCatalog.Department, TemplateLifecycleStatusCatalog.Published),
                ("other-company", 8, "C2", "D1", TemplateShareScopeCatalog.Company, TemplateLifecycleStatusCatalog.Published),
                ("other-department", 8, "C1", "D2", TemplateShareScopeCatalog.Department, TemplateLifecycleStatusCatalog.Published),
                ("same-department-code-other-company", 8, "C2", "D1", TemplateShareScopeCatalog.Department, TemplateLifecycleStatusCatalog.Published),
                ("private", 8, "C1", "D1", TemplateShareScopeCatalog.Private, TemplateLifecycleStatusCatalog.Published),
                ("draft", 8, "C1", "D1", TemplateShareScopeCatalog.All, TemplateLifecycleStatusCatalog.Draft),
                ("disabled", 8, "C1", "D1", TemplateShareScopeCatalog.All, TemplateLifecycleStatusCatalog.Disabled),
                ("unknown-share", 8, "C1", "D1", "unknown", TemplateLifecycleStatusCatalog.Published)
            };
            foreach (var (name, owner, company, department, sharing, status) in templates)
            {
                context.EmailTemplates.Add(new EmailTemplate
                {
                    Name = name,
                    OwnerUserId = owner,
                    CompanyScope = company,
                    DepartmentId = department,
                    ShareScope = sharing,
                    Status = status
                });
                context.UserReportTemplates.Add(new UserReportTemplate
                {
                    Name = name,
                    OwnerUserId = owner,
                    CompanyScope = company,
                    DepartmentId = department,
                    ShareScope = sharing,
                    Status = status
                });
            }
            await context.SaveChangesAsync();
            var user = new User
            {
                Id = 7,
                Role = UserRoleCatalog.User,
                CompanyScope = "C1",
                DepartmentId = "D1",
                EffectivePermissionGrants = new Dictionary<string, string>
                {
                    [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.EmailTemplates, PermissionAction.View)] = dataScope,
                    [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.ReportTemplates, PermissionAction.View)] = dataScope
                }
            };
            var scope = new BusinessDataAccessScope(CreatePostgreSqlModeSettings(), new FixedCurrentUserContext(user));
            string[] expected = dataScope switch
            {
                "" => [],
                PermissionDataScope.Own => ["own"],
                _ => ["all", "company", "department", "own"]
            };

            Assert.Equal(expected, await scope.ApplyEmailTemplateScope(context.EmailTemplates)
                .OrderBy(item => item.Name).Select(item => item.Name).ToArrayAsync());
            Assert.Equal(expected, await scope.ApplyUserReportTemplateScope(context.UserReportTemplates)
                .OrderBy(item => item.Name).Select(item => item.Name).ToArrayAsync());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BusinessScopes_ShouldRemainServerSideQueriesForBothDatabaseProviders(bool usePostgreSql)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            if (usePostgreSql) options.UseNpgsql("Host=localhost;Database=scope_contract;Username=test");
            else options.UseSqlite("Data Source=:memory:");
            using var context = new AppDbContext(options.Options);
            var user = new User
            {
                Id = 7,
                Role = UserRoleCatalog.User,
                CompanyScope = "C1",
                DepartmentId = "D1",
                EffectivePermissionGrants = PermissionResourceCatalog.Resources
                    .SelectMany(resource => resource.Actions.Select(action => new KeyValuePair<string, string>(
                        PermissionResourceCatalog.CreateGrantKey(resource.Key, action.Key), PermissionDataScope.Department)))
                    .ToDictionary(item => item.Key, item => item.Value)
            };
            var scope = new BusinessDataAccessScope(CreatePostgreSqlModeSettings(), new FixedCurrentUserContext(user));
            IQueryable<int>[] queries =
            [
                scope.ApplyInvoiceScope(context.Invoices).Select(item => item.Id),
                scope.ApplyPaymentScope(context.Payments).Select(item => item.Id),
                scope.ApplyCustomerScope(context.Customers).Select(item => item.Id),
                scope.ApplyExporterScope(context.Exporters).Select(item => item.Id),
                scope.ApplyPayeeScope(context.Payees).Select(item => item.Id),
                scope.ApplyCrmCustomerScope(context.CrmCustomers).Select(item => item.Id),
                scope.ApplyCrmFollowUpScope(context.CrmFollowUps).Select(item => item.Id),
                scope.ApplySupplierScope(context.SupplierCompanies).Select(item => item.Id),
                scope.ApplySalesOpportunityScope(context.SalesOpportunities).Select(item => item.Id),
                scope.ApplyContainerProjectScope(context.ContainerProjects).Select(item => item.Id),
                scope.ApplyEmailTemplateScope(context.EmailTemplates).Select(item => item.Id),
                scope.ApplyUserReportTemplateScope(context.UserReportTemplates).Select(item => item.Id),
                scope.ApplyOwnedEmailTemplateScope(context.EmailTemplates).Select(item => item.Id),
                scope.ApplyOwnedUserReportTemplateScope(context.UserReportTemplates).Select(item => item.Id)
            ];
            foreach (var query in queries)
            {
                Assert.Contains("WHERE", query.ToQueryString(), StringComparison.Ordinal);
            }
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


    }
}

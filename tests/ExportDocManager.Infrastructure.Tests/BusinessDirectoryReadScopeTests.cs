using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Opportunities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Suppliers;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class BusinessDirectoryReadScopeTests
{
    [Theory]
    [InlineData(DatabaseConnectionSettings.PostgreSqlProvider, null, false, false)]
    [InlineData(DatabaseConnectionSettings.PostgreSqlProvider, PermissionDataScope.Own, false, false)]
    [InlineData(DatabaseConnectionSettings.PostgreSqlProvider, PermissionDataScope.Department, true, false)]
    [InlineData(DatabaseConnectionSettings.PostgreSqlProvider, PermissionDataScope.Company, true, false)]
    [InlineData(DatabaseConnectionSettings.PostgreSqlProvider, PermissionDataScope.All, true, true)]
    [InlineData(DatabaseConnectionSettings.SqliteProvider, null, false, false)]
    [InlineData(DatabaseConnectionSettings.SqliteProvider, PermissionDataScope.All, true, true)]
    public async Task EmailVariables_ShouldUseTheSameContactScopeAsTheDirectory(string provider, string? contactScope, bool sameCompany, bool otherCompany)
    {
        using var factory = new InMemoryTestDatabase();
        await using (var context = factory.CreateDbContext())
        {
            context.CrmCustomers.AddRange(
                new CrmCustomer { Id = 201, Name = "Visible customer", OwnerUserId = 8, CompanyScope = "ACME", DepartmentId = "SALES" },
                new CrmCustomer { Id = 202, Name = "Other company", OwnerUserId = 8, CompanyScope = "OTHER", DepartmentId = "SALES" });
            context.CrmContacts.AddRange(
                new CrmContact { Id = 301, CrmCustomerId = 201, Name = "Private contact", Email = "contact@example.test", IsPrimary = true },
                new CrmContact { Id = 302, CrmCustomerId = 202, Name = "Other contact", Email = "other@example.test", IsPrimary = true });
            await context.SaveChangesAsync();
        }
        var user = new User { Id = 7, Username = "reader", Role = "Sales", CompanyScope = "ACME", DepartmentId = "SALES" };
        var grants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.CrmCustomers, PermissionAction.View)] = PermissionDataScope.All
        };
        if (contactScope != null) grants[PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.CrmContacts, PermissionAction.View)] = contactScope;
        user.EffectivePermissionGrants = grants;
        var scope = new BusinessDataAccessScope(new DatabaseConnectionSettings { Provider = provider }, new FixedUserContext(user));
        var service = new CrmService(factory, scope);
        foreach (var (customerId, canRead) in new[] { (201, sameCompany), (202, otherCompany) })
        {
            var contacts = await service.QueryContactsAsync(customerId, 1, 20);
            var draft = await service.GetEmailVariableDraftAsync(customerId);
            Assert.Equal(canRead ? 1 : 0, contacts.TotalCount);
            Assert.Equal(canRead, draft.CrmContactId.HasValue);
            Assert.Equal(canRead ? contacts.Items[0].Name : string.Empty, draft.Variables["ContactName"]);
            Assert.Equal(canRead ? contacts.Items[0].Email : string.Empty, draft.ToAddress);
        }
    }

    [Fact]
    public async Task DetailNavigation_ShouldReadExactRecordAndRespectEachViewScope()
    {
        using var factory = new InMemoryTestDatabase();
        await using (var context = factory.CreateDbContext())
        {
            context.CrmCustomers.AddRange(
                new CrmCustomer { Id = 201, Name = "Owned customer", OwnerUserId = 7, CompanyScope = "ACME", DepartmentId = "SALES" },
                new CrmCustomer { Id = 202, Name = "Other customer", OwnerUserId = 8, CompanyScope = "OTHER", DepartmentId = "SALES" });
            context.SupplierCompanies.AddRange(
                new SupplierCompany { Id = 201, Name = "Company supplier", OwnerUserId = 8, CompanyScope = "ACME", DepartmentId = "OTHER" },
                new SupplierCompany { Id = 202, Name = "Other supplier", OwnerUserId = 8, CompanyScope = "OTHER", DepartmentId = "SALES" });
            context.SalesOpportunities.AddRange(
                new SalesOpportunity { Id = 201, Title = "Owned opportunity", CrmCustomerId = 201, OwnerUserId = 7, CompanyScope = "ACME", DepartmentId = "SALES" },
                new SalesOpportunity { Id = 202, Title = "Other opportunity", CrmCustomerId = 201, OwnerUserId = 8, CompanyScope = "ACME", DepartmentId = "SALES" },
                new SalesOpportunity { Id = 203, Title = "Hidden customer", CrmCustomerId = 202, OwnerUserId = 7, CompanyScope = "ACME", DepartmentId = "SALES" });
            await context.SaveChangesAsync();
        }

        var user = new User { Id = 7, Username = "reader", Role = "Sales", CompanyScope = "ACME", DepartmentId = "SALES" };
        user.EffectivePermissionGrants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.CrmCustomers, PermissionAction.View)] = PermissionDataScope.Own,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.CrmCustomers, PermissionAction.Edit)] = PermissionDataScope.All,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.Suppliers, PermissionAction.View)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.SalesOpportunities, PermissionAction.View)] = PermissionDataScope.Own,
            [PermissionResourceCatalog.CreateGrantKey(PermissionModuleCatalog.CommonProductReference, PermissionAction.View)] = PermissionDataScope.All
        };
        var scope = new BusinessDataAccessScope(new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.PostgreSqlProvider }, new FixedUserContext(user));
        var customers = new CrmService(factory, scope);
        var suppliers = new SupplierDirectoryService(factory, scope);
        var opportunities = new SalesOpportunityService(factory, scope);
        Assert.Equal("Owned customer", (await customers.GetCustomerAsync(201)).Name);
        Assert.Equal("Company supplier", (await suppliers.GetAsync(201)).Name);
        Assert.Equal("Owned opportunity", (await opportunities.GetAsync(201)).Title);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => customers.GetCustomerAsync(202));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => suppliers.GetAsync(202));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => opportunities.GetAsync(202));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => opportunities.GetAsync(203));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => customers.GetCustomerAsync(999));
    }

    private sealed class FixedUserContext(User user) : ICurrentUserContext
    {
        public User? CurrentUser => user;
    }
}

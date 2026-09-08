using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Worklist;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class WorklistServiceTests
{
    [Fact]
    public async Task Paging_ShouldReadEveryPersonalRecordWithoutSummaryLimitsOrFinancialInferences()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateDbContext())
        {
            context.Invoices.AddRange(Enumerable.Range(1, 23).Select(number => BusinessFeatureTestRuntime.Invoice("OWN-" + number)));
            context.Invoices.Add(BusinessFeatureTestRuntime.Invoice("OTHER", 8));
            var shipped = BusinessFeatureTestRuntime.Invoice("SHIPPED");
            shipped.Status = InvoiceStatusCatalog.Shipped;
            context.Invoices.Add(shipped);
            var customer = new CrmCustomer { Name = "Customer", OwnerUserId = 7 };
            context.CrmCustomers.Add(customer);
            await context.SaveChangesAsync();
            context.CrmFollowUps.AddRange(Enumerable.Range(1, 27).Select(number => new CrmFollowUp
            { CrmCustomerId = customer.Id, OwnerUserId = 7, Summary = "Follow " + number, NextFollowUpAt = BusinessFeatureTestRuntime.Clock.UtcNow.AddHours(-number) }));
            await context.SaveChangesAsync();
        }
        var service = Service(db);
        var rows = new List<WorklistItem>();
        for (int page = 1; page <= 5; page++)
        {
            var result = await service.QueryAsync(new(PageNumber: page, PageSize: 10));
            Assert.Equal(50, result.Page.TotalCount);
            Assert.Equal(50, result.Sources.Sum(source => source.Count));
            rows.AddRange(result.Page.Items);
        }
        Assert.Equal(50, rows.Select(row => (row.Source, row.RecordId)).Distinct().Count());
        Assert.DoesNotContain(rows, row => row.Title is "OTHER" or "SHIPPED");
        Assert.Equal(23, rows.Count(row => row.Source == "invoice-review"));
        using (var context = db.CreateDbContext())
        {
            foreach (var invoice in context.Invoices) invoice.Status = InvoiceStatusCatalog.Verified;
            foreach (var followUp in context.CrmFollowUps) followUp.IsCompleted = true;
            await context.SaveChangesAsync();
        }
        Assert.Equal(0, (await service.QueryAsync(new())).Page.TotalCount);
    }

    [Fact]
    public async Task DueFilters_ShouldUseBusinessDatesAndNeverMarkUndatedItemsOverdue()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateDbContext())
        {
            var customer = new CrmCustomer { Name = "Customer", OwnerUserId = 7 };
            context.CrmCustomers.Add(customer);
            await context.SaveChangesAsync();
            foreach (var due in new DateTimeOffset?[] { null, BusinessFeatureTestRuntime.Clock.UtcNow.AddMinutes(-1), BusinessFeatureTestRuntime.Clock.UtcNow.AddDays(7), BusinessFeatureTestRuntime.Clock.UtcNow.AddDays(31) })
                context.CrmFollowUps.Add(new CrmFollowUp { OwnerUserId = 7, CrmCustomerId = customer.Id, Summary = "follow-up", NextFollowUpAt = due });
            context.Invoices.Add(BusinessFeatureTestRuntime.Invoice("UNDATED"));
            await context.SaveChangesAsync();
        }
        var service = Service(db);
        var overdue = await service.QueryAsync(new(Due: WorklistDueFilter.Overdue));
        Assert.True(Assert.Single(overdue.Page.Items).IsOverdue);
        Assert.Single((await service.QueryAsync(new(Due: WorklistDueFilter.Upcoming))).Page.Items);
        var undated = await service.QueryAsync(new(Due: WorklistDueFilter.Undated));
        Assert.Equal(2, undated.Page.TotalCount);
        Assert.All(undated.Page.Items, item => Assert.False(item.IsOverdue));
    }

    [Fact]
    public async Task MissingSourcesAndDisabledWorkspaces_ShouldNotBecomeMandatoryDependencies()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateDbContext()) { context.Invoices.Add(BusinessFeatureTestRuntime.Invoice("DOCUMENT")); await context.SaveChangesAsync(); }
        var actor = BusinessFeatureTestRuntime.Admin();
        var scope = BusinessFeatureTestRuntime.Scope(actor);
        var empty = new WorklistService(db, scope, BusinessFeatureTestRuntime.Clock, []);
        Assert.Empty((await empty.QueryAsync(new())).Sources);
        var sales = new WorklistService(db, scope, BusinessFeatureTestRuntime.Clock,
            [new InvoiceWorklistSource(scope, new BusinessFeatureTestRuntime(ProductEditionCatalog.Sales, false))]);
        Assert.Empty((await sales.QueryAsync(new())).Sources);
        var reader = new User
        {
            Id = 7,
            Username = "reader",
            Role = "User",
            IsActive = true,
            EffectivePermissionGrants = new Dictionary<string, string>
            { [PermissionResourceCatalog.CreateGrantKey(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View)] = PermissionDataScope.All }
        };
        Assert.Empty((await Service(db, reader).QueryAsync(new())).Sources);
    }

    [Fact]
    public async Task PersonnelReminders_ShouldRequirePrivatePermissionAndRespectCompanyBoundary()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateDbContext())
        {
            foreach (string company in new[] { "C1", "C2" })
            {
                context.OrganizationCompanies.Add(new OrganizationCompany { Code = company, Name = company });
                context.OrganizationDepartments.Add(new OrganizationDepartment { Code = company + "-D", CompanyCode = company, Name = "Department" });
                context.PersonnelEmployees.Add(new PersonnelEmployee
                {
                    CompanyScope = company,
                    DepartmentId = company + "-D",
                    FullName = company,
                    EmployeeNumber = "001",
                    RequestKey = Guid.NewGuid(),
                    Status = EmploymentStatus.Probation,
                    HireDate = BusinessFeatureTestRuntime.Clock.Today.AddYears(-1),
                    LastEffectiveDate = BusinessFeatureTestRuntime.Clock.Today.AddYears(-1),
                    ProbationEndsOn = BusinessFeatureTestRuntime.Clock.Today.AddDays(-1),
                    ContractEndsOn = BusinessFeatureTestRuntime.Clock.Today
                });
            }
            await context.SaveChangesAsync();
        }
        var actor = BusinessFeatureTestRuntime.Admin();
        var page = await Service(db, actor, office: true).QueryAsync(new());
        Assert.Equal(2, page.Page.TotalCount);
        Assert.All(page.Page.Items, item => Assert.Equal("C1", item.Title));
        Assert.Single((await Service(db, actor, office: true).QueryAsync(new(Due: WorklistDueFilter.Overdue))).Page.Items);
        actor.Role = "User";
        actor.EffectivePermissionGrants = new Dictionary<string, string>
        { [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.OfficePeople, PermissionAction.View)] = PermissionDataScope.All };
        Assert.Empty((await Service(db, actor, office: true).QueryAsync(new())).Page.Items);
    }

    private static WorklistService Service(SqliteTestDatabase db, User? actor = null, bool office = false)
    {
        var scope = BusinessFeatureTestRuntime.Scope(actor ?? BusinessFeatureTestRuntime.Admin());
        var runtime = new BusinessFeatureTestRuntime(team: office);
        return new WorklistService(db, scope, BusinessFeatureTestRuntime.Clock,
            [new InvoiceWorklistSource(scope, runtime), new FollowUpWorklistSource(scope, runtime), new OfficeWorklistSource(scope, runtime)]);
    }

    [Fact]
    public async Task OfficeTasks_ShouldSeparateApprovalsFromPersonalHandoverAndDisappearOnReturn()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateDbContext())
        {
            context.OrganizationCompanies.Add(new OrganizationCompany { Code = "C1", Name = "公司一" });
            var supply = new OfficeSupply { CompanyScope = "C1", Name = "投影仪", IsReturnable = true };
            context.OfficeSupplies.Add(supply);
            await context.SaveChangesAsync();
            context.OfficeSupplyRequests.AddRange(
                new OfficeSupplyRequest { CompanyScope = "C1", OfficeSupplyId = supply.Id, OwnerUserId = 8, RequestKey = Guid.NewGuid(), Quantity = 1, Status = SupplyRequestStatus.Pending },
                new OfficeSupplyRequest { CompanyScope = "C1", OfficeSupplyId = supply.Id, OwnerUserId = 7, RequestKey = Guid.NewGuid(), Quantity = 1, Status = SupplyRequestStatus.Pending },
                new OfficeSupplyRequest { CompanyScope = "C1", OfficeSupplyId = supply.Id, OwnerUserId = 7, RequestKey = Guid.NewGuid(), Quantity = 1, Status = SupplyRequestStatus.Approved },
                new OfficeSupplyRequest
                {
                    CompanyScope = "C1",
                    OfficeSupplyId = supply.Id,
                    OwnerUserId = 7,
                    RequestKey = Guid.NewGuid(),
                    Quantity = 2,
                    ReturnedQuantity = 1,
                    Status = SupplyRequestStatus.Issued,
                    ReturnDueDate = BusinessFeatureTestRuntime.Clock.Today.AddDays(-1)
                });
            await context.SaveChangesAsync();
        }
        var service = Service(db, office: true);
        var page = await service.QueryAsync(new());
        Assert.Single(page.Page.Items, item => item.Source == "supply-approval");
        Assert.Single(page.Page.Items, item => item.Source == "supply-collection");
        Assert.True(Assert.Single(page.Page.Items, item => item.Source == "supply-return").IsOverdue);
        using (var context = db.CreateDbContext())
        {
            var request = await context.OfficeSupplyRequests.SingleAsync(item => item.Status == SupplyRequestStatus.Issued);
            request.Status = SupplyRequestStatus.Returned;
            request.ReturnedQuantity = request.Quantity;
            await context.SaveChangesAsync();
        }
        Assert.DoesNotContain((await service.QueryAsync(new())).Page.Items, item => item.Source == "supply-return");
    }

}

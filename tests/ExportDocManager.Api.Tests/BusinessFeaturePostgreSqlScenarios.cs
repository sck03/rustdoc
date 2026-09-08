using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Attachments;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Services.Worklist;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests;

internal static class BusinessFeaturePostgreSqlScenarios
{
    internal static async Task RunAsync(DatabaseConnectionSettings settings, User actor)
    {
        var factory = new RetryEnabledFactory(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DbHelper.BuildPostgreSqlConnectionString(settings), provider => provider.EnableRetryOnFailure()).Options);
        var access = new BusinessDataAccessScope(settings, new CurrentActor(actor));
        var runtime = new ApiAuthorizationService(new ApiRuntimeOptions { NetworkMode = true }, new RuntimeCapabilitySet(CapabilityModuleKeys.All));
        var clock = new BusinessClock(new FixedTime(), "Asia/Shanghai");
        var service = new BusinessAttachmentService(factory, access, clock, runtime);
        int invoiceId;
        await using (var db = factory.CreateDbContext())
        {
            var invoice = new Invoice
            {
                InvoiceNo = "PG-ARCHIVE",
                CustomerNameEN = "Café Company",
                OwnerUserId = actor.Id,
                CompanyScope = actor.CompanyScope ?? "",
                DepartmentId = actor.DepartmentId ?? "",
                InvoiceDate = clock.Today,
                ShipmentDate = clock.Today
            };
            db.Invoices.Add(invoice);
            var customer = new CrmCustomer { Name = "PG feature customer", OwnerUserId = actor.Id, CompanyScope = actor.CompanyScope ?? "" };
            db.CrmCustomers.Add(customer);
            await db.SaveChangesAsync();
            invoiceId = invoice.Id;
            db.CrmFollowUps.Add(new CrmFollowUp
            {
                CrmCustomerId = customer.Id,
                OwnerUserId = actor.Id,
                CompanyScope = actor.CompanyScope ?? "",
                Summary = "待确认图纸",
                NextFollowUpAt = clock.UtcNow.AddMinutes(-1)
            });
            db.PersonnelEmployees.Add(new PersonnelEmployee
            {
                CompanyScope = actor.CompanyScope!,
                DepartmentId = actor.DepartmentId!,
                EmployeeNumber = "PG-ATT-001",
                FullName = "归档验收员工",
                RequestKey = Guid.NewGuid(),
                Status = EmploymentStatus.Probation,
                HireDate = clock.Today.AddMonths(-3),
                LastEffectiveDate = clock.Today.AddMonths(-3),
                ProbationEndsOn = clock.Today.AddDays(-1),
                ContractEndsOn = clock.Today
            });
            await db.SaveChangesAsync();
        }
        var request = new BusinessAttachmentUpload(null, 0, Guid.NewGuid(), "原始图纸", BusinessAttachmentCategory.Original, "PO-TEST", "STYLE-TEST", "cafe\u0301.txt", "首次上传");
        var repeated = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => service.UploadAsync(invoiceId, request, new MemoryStream("original"u8.ToArray()))));
        Assert.Equal(repeated[0].Id, repeated[1].Id);
        Assert.Single((await service.GetAsync(repeated[0].Id)).Revisions);
        var races = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            try
            {
                return (object)await service.UploadAsync(invoiceId, request with
                { AttachmentId = repeated[0].Id, ExpectedVersion = repeated[0].VersionNumber, UploadKey = Guid.NewGuid(), FileName = $"updated-{index}.txt" },
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes("update " + index)));
            }
            catch (ResourceConflictException error) { return error; }
        }));
        Assert.Single(races.OfType<BusinessAttachmentRecord>());
        Assert.Single(races.OfType<ResourceConflictException>());
        Assert.Equal("original", System.Text.Encoding.UTF8.GetString((await service.ReadAsync(repeated[0].Id, 1)).Content));
        Assert.Single((await service.QueryAsync(new(Keyword: "CAFÉ"))).Page.Items);
        var worklist = new WorklistService(factory, access, clock,
            [new InvoiceWorklistSource(access, runtime), new FollowUpWorklistSource(access, runtime), new OfficeWorklistSource(access, runtime)]);
        Assert.Contains((await worklist.QueryAsync(new())).Page.Items, item => item.Source == "invoice-review" && item.RecordId == invoiceId);
        var overdue = await worklist.QueryAsync(new(Due: WorklistDueFilter.Overdue));
        Assert.Contains(overdue.Page.Items, item => item.Source == "customer-follow-up");
        Assert.Contains(overdue.Page.Items, item => item.Source == "probation-end");
        Assert.DoesNotContain(overdue.Page.Items, item => item.Source == "contract-end" && item.Title == "归档验收员工");
        Assert.Equal(new DateOnly(2026, 9, 8), overdue.BusinessDate);
        Assert.Equal(TimeSpan.Zero, overdue.AsOf.Offset);
        var upcoming = await worklist.QueryAsync(new(Due: WorklistDueFilter.Upcoming));
        Assert.Contains(upcoming.Page.Items, item => item.Source == "contract-end" && item.Title == "归档验收员工");
        Assert.DoesNotContain(upcoming.Page.Items, item => item.Source == "customer-follow-up");
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 7, 16, 30, 0, TimeSpan.Zero);
    }

    private sealed class RetryEnabledFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
    private sealed class CurrentActor(User actor) : ICurrentUserContext
    {
        public User CurrentUser => actor;
    }
}

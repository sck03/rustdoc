using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests;

internal static partial class PersonnelPostgreSqlScenarios
{
    private static async Task VerifyMaintenanceAsync(IDbContextFactory<AppDbContext> factory, PersonnelService people, User admin, IBusinessClock clock)
    {
        var directory = new OrganizationDirectoryService(factory, new PersonnelUser(admin));
        var unusedCompany = await directory.SaveCompanyAsync(new("", "PG-UNUSED", "误建公司", true));
        await directory.DeleteCompanyAsync(unusedCompany.Code, new(unusedCompany.VersionNumber, "误建"));
        var department = await directory.SaveDepartmentAsync(new("", "PG-REFERENCED", admin.CompanyScope!, "付款归属", true));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Payments.Add(new Payment { CompanyScope = admin.CompanyScope!, DepartmentId = department.Code, InvoiceNo = "PG-SCOPE", Spare10 = "付款独立字段" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ResourceConflictException>(() => directory.DeleteDepartmentAsync(department.Code, new(department.VersionNumber, "有业务引用")));
        var person = await people.CreateAsync(new(Guid.NewGuid(), "PG-MISENTRY", admin.DepartmentId!, "测试岗位", EmploymentType.FullTime,
            clock.Today, true, null, null, new("误录档案")));
        var corrections = await RaceAsync(2, index => people.UpdateAsync(person.Employee.Id, new(person.VersionNumber,
            person.Profile with { WorkPhone = "100" + index }, person.EmploymentType, null, null)));
        Assert.Single(corrections.OfType<PersonnelChangeResult>());
        Assert.Single(corrections.OfType<ServiceConcurrencyException>());
        person = corrections.OfType<PersonnelChangeResult>().Single().Record;
        var deletions = await RaceAsync<object>(2, async _ =>
        {
            await people.DeleteAsync(person.Employee.Id, new(person.VersionNumber, "误录"));
            return "deleted";
        });
        Assert.Single(deletions.OfType<string>());
        Assert.Single(deletions.OfType<ResourceNotFoundException>());
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.False(await db.PersonnelEmployees.AnyAsync(item => item.Id == person.Employee.Id));
            Assert.False(await db.PersonnelEvents.AnyAsync(item => item.EmployeeId == person.Employee.Id));
            Assert.True(await db.AuditLogs.AnyAsync(item => item.EntityName == nameof(PersonnelEmployee) && item.EntityId == person.Employee.Id.ToString() && item.Action == "Delete"));
            Assert.Equal("付款独立字段", (await db.Payments.SingleAsync(item => item.InvoiceNo == "PG-SCOPE")).Spare10);
        }
        Console.WriteLine("Personnel PostgreSQL maintenance: scope references, concurrent edits, single deletion and retained audit passed.");
    }
}

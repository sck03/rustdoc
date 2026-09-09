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
    private static async Task VerifyImagesAndOrganizationAsync(IDbContextFactory<AppDbContext> factory, PersonnelService people, User admin, IBusinessClock clock)
    {
        var directory = new OrganizationDirectoryService(factory, new PersonnelUser(admin));
        var parent = await directory.SaveDepartmentAsync(new("", "PEOPLE-PG-A", admin.CompanyScope!, "架构 A", true));
        var child = await directory.SaveDepartmentAsync(new("", "PEOPLE-PG-B", admin.CompanyScope!, "架构 B", true));
        var hierarchyRace = await RaceAsync(2, index => directory.SaveDepartmentAsync(index == 0
            ? new(parent.Code, parent.Code, parent.CompanyCode, parent.Name, true, parent.VersionNumber, child.Code)
            : new(child.Code, child.Code, child.CompanyCode, child.Name, true, child.VersionNumber, parent.Code)));
        Assert.Single(hierarchyRace.OfType<OrganizationDepartmentRecord>());
        Assert.Single(hierarchyRace.OfType<ServiceException>());
        var person = await people.CreateAsync(new(Guid.NewGuid(), "EMP-PG-MANAGER", admin.DepartmentId!, "部门负责人", EmploymentType.FullTime,
            clock.Today, false, null, null, new PersonnelProfile("组织负责人", IdentityNumber: "11010519491231002X")));
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4i8AAAAASUVORK5CYII=");
        var imageRace = await RaceAsync(2, async index =>
        {
            using var source = new MemoryStream(bytes);
            return await people.SaveImageAsync(person.Employee.Id, index == 0 ? PersonnelImageKind.Avatar : PersonnelImageKind.IdentityFront,
                person.VersionNumber, source, "image/png");
        });
        person = Assert.Single(imageRace.OfType<PersonnelRecord>());
        Assert.Single(imageRace.OfType<ServiceConcurrencyException>());
        Assert.Equal(bytes, (await people.ReadImageAsync(person.Employee.Id, Assert.Single(person.Images).Kind)).Content);
        Assert.Equal("11010519491231002X", (await people.GetAsync(person.Employee.Id)).Profile.IdentityNumber);
        var managed = await directory.SaveDepartmentAsync(new("", "PEOPLE-PG-MANAGED", admin.CompanyScope!, "管理职责", true));
        var assignmentRace = await RaceAsync<object>(2, async index => index == 0
            ? await directory.SaveDepartmentAsync(new(managed.Code, managed.Code, managed.CompanyCode, managed.Name, true, managed.VersionNumber,
                ManagerEmployeeId: person.Employee.Id))
            : await people.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, clock.Today, "负责人并发离职")));
        Assert.Single(assignmentRace.OfType<ServiceException>());
        await using var db = factory.CreateDbContext();
        var saved = await db.PersonnelEmployees.SingleAsync(item => item.Id == person.Employee.Id);
        bool assigned = await db.OrganizationDepartments.AnyAsync(item => item.ManagerEmployeeId == person.Employee.Id);
        Assert.False(assigned && saved.Status == EmploymentStatus.Departed);
        Console.WriteLine("Personnel PostgreSQL: identity/images round trip, stale image writes, competing hierarchy moves and manager/departure race passed.");
    }
}

using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class OrganizationHierarchyTests
{
    [Fact]
    public async Task Hierarchy_ShouldRejectCyclesAndForeignParents_AndPreserveStaleWriteProtection()
    {
        using var env = new PersonnelTestEnvironment();
        var service = new OrganizationDirectoryService(env.Database, env.Actor);
        var child = await service.SaveDepartmentAsync(new("", "CHILD", "C1", "销售一部", true, ParentCode: "d1"));
        Assert.Equal("D1", child.ParentCode);
        var parent = (await service.ListAsync()).Departments.Single(item => item.Code == "D1");
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.SaveDepartmentAsync(new("D1", "D1", "C1", "业务部", true, parent.VersionNumber, "CHILD")));
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.SaveDepartmentAsync(new("CHILD", "CHILD", "C1", "销售一部", true, child.VersionNumber, "D3")));
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.SaveDepartmentAsync(new("CHILD", "CHILD", "C2", "销售一部", true, child.VersionNumber)));
        child = await service.SaveDepartmentAsync(new("CHILD", "CHILD", "C1", "华东销售", true, child.VersionNumber, "D1"));
        await Assert.ThrowsAsync<BusinessConcurrencyException>(() => service.SaveDepartmentAsync(new("CHILD", "CHILD", "C1", "过期名称", true, child.VersionNumber - 1, "D1")));
        Assert.Contains((await env.People.OptionsAsync()).Departments, item => item.Code == child.Code && item.ParentCode == "D1" && item.Name == "华东销售");
        env.AsEmployee();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.ListAsync());
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.ManagerOptionsAsync("C1", null, 1, 20));
    }

    [Fact]
    public async Task ActiveChildren_ShouldBlockParentDeactivation()
    {
        using var env = new PersonnelTestEnvironment();
        var service = new OrganizationDirectoryService(env.Database, env.Actor);
        var parent = await service.SaveDepartmentAsync(new("", "ROOT", "C1", "总部", true));
        var child = await service.SaveDepartmentAsync(new("", "CHILD", "C1", "分部", true, ParentCode: parent.Code));
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.SaveDepartmentAsync(new(parent.Code, parent.Code, "C1", parent.Name, false, parent.VersionNumber)));
        await service.SaveDepartmentAsync(new(child.Code, child.Code, "C1", child.Name, false, child.VersionNumber, parent.Code));
        parent = await service.SaveDepartmentAsync(new(parent.Code, parent.Code, "C1", parent.Name, false, parent.VersionNumber));
        Assert.False(parent.IsActive);
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.SaveDepartmentAsync(new("", "NEW", "C1", "新部门", true, ParentCode: parent.Code)));
    }

    [Fact]
    public async Task Managers_ShouldBelongToCompany_AndBeReassignedBeforeDeparture()
    {
        using var env = new PersonnelTestEnvironment();
        var service = new OrganizationDirectoryService(env.Database, env.Actor);
        var person = await env.People.CreateAsync(env.Request());
        var department = await service.SaveDepartmentAsync(new("", "MANAGED", "C1", "管理部门", true, ManagerEmployeeId: person.Employee.Id));
        Assert.Equal(person.Employee.FullName, department.ManagerName);
        var clearance = await env.People.ClearanceAsync(person.Employee.Id);
        Assert.True(clearance.IsClear);
        Assert.False(clearance.CanDepart);
        Assert.Equal(department.Code, Assert.Single(clearance.ManagedDepartments).Code);
        Assert.Equal(person.Employee.Id, Assert.Single((await service.ManagerOptionsAsync("C1", "EMP-001", 1, 20)).Items).Id);
        Assert.Empty((await service.ManagerOptionsAsync("C2", null, 1, 20)).Items);
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.SaveDepartmentAsync(new("", "FOREIGN", "C2", "其他公司部门", true, ManagerEmployeeId: person.Employee.Id)));
        await Assert.ThrowsAsync<ResourceConflictException>(() => env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "离职")));
        await service.SaveDepartmentAsync(new(department.Code, department.Code, "C1", department.Name, true, department.VersionNumber));
        person = (await env.People.TransitionAsync(person.Employee.Id, PersonnelAction.Depart, new(person.VersionNumber, env.Today, "交接完成"))).Record;
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.SaveDepartmentAsync(new("", "DEPARTED", "C1", "离职负责人", true, ManagerEmployeeId: person.Employee.Id)));
        Assert.Empty((await service.ManagerOptionsAsync("C1", null, 1, 20)).Items);
    }

    [Fact]
    public async Task Database_ShouldRejectCrossCompanyParentAndManagerReferences()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.People.CreateAsync(env.Request());
        await using (var db = env.Database.CreateDbContext())
        {
            db.OrganizationDepartments.Add(new OrganizationDepartment { Code = "BAD-PARENT", CompanyCode = "C1", Name = "错误上级", ParentCode = "D3" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = env.Database.CreateDbContext())
        {
            db.OrganizationDepartments.Add(new OrganizationDepartment { Code = "BAD-MANAGER", CompanyCode = "C2", Name = "错误负责人", ManagerEmployeeId = person.Employee.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}

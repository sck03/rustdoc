using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

internal sealed class PersonnelTestEnvironment : IDisposable
{
    internal SqliteTestDatabase Database { get; } = new();
    internal PersonnelUserContext Actor { get; } = new();
    internal DatabaseConnectionSettings Settings { get; } = new() { Provider = DatabaseConnectionSettings.PostgreSqlProvider };
    internal User Admin { get; } = new() { Id = 1, Username = "hr-admin", FullName = "人事管理员", Role = UserRoleCatalog.Admin, CompanyScope = "C1", DepartmentId = "D1" };
    internal User Account { get; } = new() { Id = 2, Username = "employee", FullName = "张宁", CompanyScope = "C1", DepartmentId = "D1" };
    internal OfficeServiceContext Office { get; }
    internal PersonnelService People { get; }
    internal DateOnly Today => Office.Clock.Today;

    internal PersonnelTestEnvironment(OfficeOperatingMode mode = OfficeOperatingMode.Team)
    {
        if (mode == OfficeOperatingMode.LocalRegister) Settings.Provider = DatabaseConnectionSettings.SqliteProvider;
        Office = new(Database, new BusinessDataAccessScope(Settings, Actor), new BusinessClock(new PersonnelTimeProvider(), "Asia/Shanghai"), mode);
        People = new(Office);
        using var db = Database.CreateDbContext();
        db.OrganizationCompanies.AddRange(new OrganizationCompany { Code = "C1", Name = "公司一" }, new OrganizationCompany { Code = "C2", Name = "公司二" });
        db.OrganizationDepartments.AddRange(new OrganizationDepartment { Code = "D1", CompanyCode = "C1", Name = "业务部" },
            new OrganizationDepartment { Code = "D2", CompanyCode = "C1", Name = "运营部" },
            new OrganizationDepartment { Code = "D3", CompanyCode = "C2", Name = "另一公司" });
        db.Users.AddRange(Admin, Account);
        db.PermissionTemplates.Add(new PermissionTemplate { Code = BuiltInPermissionTemplateCatalog.Document, Name = "单证人员", IsActive = true, IsSystem = true });
        db.SaveChanges();
        AsAdmin();
    }

    internal PersonnelCreateRequest Request(string number = "EMP-001", string department = "D1") => new(Guid.NewGuid(), number, department,
        "业务专员", EmploymentType.FullTime, new(2026, 8, 1), true, new(2026, 9, 30), new(2027, 7, 31),
        new("张宁", "staff@example.test", "010-12345678", "三楼", "private-phone", "家属", "emergency-phone", "仅人事可见"));

    internal async Task<PersonnelRecord> LinkedAsync()
    {
        var employee = await People.CreateAsync(Request());
        return (await People.LinkAccountAsync(employee.Employee.Id, new(employee.VersionNumber, Account.Id, Account.VersionNumber))).Record;
    }
    internal void AsAdmin() => Actor.CurrentUser = Admin;
    internal void AsEmployee()
    {
        using var db = Database.CreateDbContext();
        Actor.CurrentUser = db.Users.AsNoTracking().Single(item => item.Id == Account.Id);
    }
    public void Dispose() => Database.Dispose();
    internal sealed class PersonnelUserContext : ICurrentUserContext { public User? CurrentUser { get; set; } }
    private sealed class PersonnelTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 7, 1, 0, 0, TimeSpan.Zero);
    }
}

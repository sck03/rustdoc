using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;

namespace ExportDocManager.Infrastructure.Tests;

internal sealed class BusinessFeatureTestRuntime(string edition = ProductEditionCatalog.Full, bool team = true) : IRuntimePermissionAvailability
{
    public OfficeOperatingMode OfficeMode => ProductEditionCatalog.OfficeMode(edition, team);
    public bool IsPermissionAvailable(string resourceKey, string action) =>
        PermissionResourceCatalog.IsKnownAction(resourceKey, action) &&
        ProductEditionCatalog.IncludesResource(edition, PermissionResourceCatalog.ByKey[resourceKey], team);

    public static readonly IBusinessClock Clock = new BusinessClock(new FixedTime(), "Asia/Shanghai");
    public static User Admin(int id = 7, string company = "C1") => new() { Id = id, Username = "admin-" + id, Role = UserRoleCatalog.Admin, CompanyScope = company, IsActive = true };
    public static BusinessDataAccessScope Scope(User user, bool team = true) => new(new DatabaseConnectionSettings
    { Provider = team ? DatabaseConnectionSettings.PostgreSqlProvider : DatabaseConnectionSettings.SqliteProvider }, new FixedCurrentUserContext(user));
    public static Invoice Invoice(string number, int owner = 7, string company = "C1") => new()
    {
        InvoiceNo = number,
        OwnerUserId = owner,
        CompanyScope = company,
        DepartmentId = "D1",
        CustomerNameEN = "Customer",
        ExporterNameEN = "Exporter",
        InvoiceDate = Clock.Today,
        ShipmentDate = Clock.Today,
        Type = "实际数据",
        Status = InvoiceStatusCatalog.Draft
    };
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 4, 0, 0, TimeSpan.Zero);
    }
}

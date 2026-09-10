using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class PersonnelService
{
    public Task DeleteAsync(int id, DeleteRecordRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Delete, true, async (db, actor, token) =>
        {
            var employee = await EmployeeAsync(db, id, actor, PermissionAction.Delete, true, token);
            Version(request.ExpectedVersion, employee.VersionNumber);
            string reason = Text(request.Reason, "删除原因", 500, true);
            string restriction = await RegistrationRestrictionAsync(db, employee, token);
            if (restriction.Length > 0) throw new ResourceConflictException(restriction);
            // Delete binary contents and registration events in the same transaction.
            // The separate audit retains only identifiers and sanitized reason metadata.
            await db.PersonnelImages.Where(item => item.EmployeeId == id).ExecuteDeleteAsync(token);
            await db.PersonnelEvents.Where(item => item.EmployeeId == id).ExecuteDeleteAsync(token);
            RecordDeletionAudit.Add(db, actor, nameof(PersonnelEmployee), id, reason, office.Clock.UtcNow);
            db.PersonnelEmployees.Remove(employee);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

    private static async Task<string> RegistrationRestrictionAsync(AppDbContext db, PersonnelEmployee employee, CancellationToken token)
    {
        if (employee.OwnerUserId.HasValue) return "该人员已关联登录账号，请保留档案并通过任职流程处理。";
        if (employee.Status == EmploymentStatus.Departed || await db.PersonnelEvents.AnyAsync(item => item.EmployeeId == employee.Id &&
            item.Action != "Hire" && item.Action != "Edit" && item.Action != "Image", token))
            return "该档案已有正式任职变动，请保留历史并通过转正、调岗或离职流程处理。";
        if (await db.MeetingBookings.AnyAsync(item => item.EmployeeId == employee.Id, token) ||
            await db.OfficeSupplyRequests.AnyAsync(item => item.EmployeeId == employee.Id, token))
            return "该人员已有预约或领用记录，请保留档案；工作信息仍可编辑。";
        if (await db.OrganizationDepartments.AnyAsync(item => item.ManagerEmployeeId == employee.Id, token))
            return "该人员仍是部门负责人，请先在组织架构中调整负责人。";
        return "";
    }

    private async Task<bool> CorrectRegistrationAsync(AppDbContext db, PersonnelEmployee employee, User actor,
        PersonnelRegistrationCorrection? correction, CancellationToken token)
    {
        if (correction == null) return false;
        string number = Text(correction.EmployeeNumber, "工号", 40, true);
        string department = Text(correction.DepartmentId, "部门", 50, true);
        string title = Text(correction.JobTitle, "岗位", 120, true);
        var status = correction.OnProbation ? EmploymentStatus.Probation : EmploymentStatus.Active;
        if (number == employee.EmployeeNumber && department == employee.DepartmentId && title == employee.JobTitle &&
            correction.HireDate == employee.HireDate && status == employee.Status) return false;
        string restriction = await RegistrationRestrictionAsync(db, employee, token);
        if (restriction.Length > 0) throw new ResourceConflictException(restriction);
        if (number.Any(char.IsWhiteSpace)) throw new ServiceValidationException("工号不能包含空白字符。");
        if (correction.HireDate < new DateOnly(1900, 1, 1) || correction.HireDate > office.Clock.Today)
            throw new ServiceValidationException("入职日期须在 1900 年至公司业务日期之间。");
        await DemandDepartmentAsync(db, department, actor, token);
        employee.EmployeeNumber = number;
        employee.DepartmentId = department;
        employee.JobTitle = title;
        employee.HireDate = employee.LastEffectiveDate = correction.HireDate;
        employee.ConfirmedOn = correction.OnProbation ? null : correction.HireDate;
        employee.Status = status;
        office.DemandRecord(employee, actor, Resource, PermissionAction.Edit);
        office.DemandRecord(employee, actor, Resource, PermissionAction.ViewDetails);
        return true;
    }
}

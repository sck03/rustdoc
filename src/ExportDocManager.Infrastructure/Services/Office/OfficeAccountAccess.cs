using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Office;

/// <summary>Account row locks serialize employee departure with new office requests.</summary>
internal static class OfficeAccountAccess
{
    internal static async Task<User> LockAsync(AppDbContext db, int userId, string company, CancellationToken token) =>
        await (db.Database.IsNpgsql()
            ? db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} AND \"CompanyScope\" = {company} FOR UPDATE")
            : db.Users.Where(item => item.Id == userId && item.CompanyScope == company))
            .SingleOrDefaultAsync(token) ?? throw new ResourceNotFoundException("同公司关联账号不存在。");

    internal static async Task DemandActiveApplicantAsync(AppDbContext db, User actor, CancellationToken token)
    {
        var account = await LockAsync(db, actor.Id, actor.CompanyScope!, token);
        if (!account.IsActive || account.VersionNumber != actor.VersionNumber || account.DepartmentId != actor.DepartmentId)
            throw new PermissionDeniedException("账号或所属组织已变更，请重新登录后再申请。");
        if (await db.PersonnelEmployees.AnyAsync(item => item.OwnerUserId == actor.Id && item.Status == EmploymentStatus.Departed, token))
            throw new PermissionDeniedException("已离职人员不能提交新的行政申请。");
    }

    internal static async Task ValidateAccountEditAsync(AppDbContext db, User previous, User next, CancellationToken token)
    {
        var employee = await db.PersonnelEmployees.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == previous.Id, token);
        if (employee != null && (next.FullName != employee.FullName || next.CompanyScope != employee.CompanyScope ||
            next.DepartmentId != employee.DepartmentId || next.IsActive && employee.Status == EmploymentStatus.Departed ||
            string.Equals(next.Role, "Admin", StringComparison.OrdinalIgnoreCase)))
            throw new ResourceConflictException("账号已关联人员档案，姓名和组织请在人事流程中维护；离职账号不能启用，关联账号不能改为管理员。");
        if (previous.CompanyScope != next.CompanyScope || previous.DepartmentId != next.DepartmentId)
            await PersonnelService.DemandClearanceAsync(db, previous.Id, token);
    }
}

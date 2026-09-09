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
    public Task<PersonnelChangeResult> TransitionAsync(int id, PersonnelAction action, PersonnelTransitionRequest request,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Transition, true, async (db, actor, token) =>
        {
            var employee = await EmployeeAsync(db, id, actor, PermissionAction.Transition, true, token);
            Version(request.ExpectedVersion, employee.VersionNumber);
            if (employee.OwnerUserId == actor.Id) throw new PermissionDeniedException("自己的任职变动须由另一位获授权的人员办理。");
            ValidateEffectiveDate(employee, request.EffectiveDate);
            string note = Text(request.Note, "办理说明", 500, true);
            string summary;
            if (action is not (PersonnelAction.Transfer or PersonnelAction.Rehire) &&
                (!string.IsNullOrWhiteSpace(request.DepartmentId) || !string.IsNullOrWhiteSpace(request.JobTitle)))
                throw new ServiceValidationException("只有调岗或返聘可以指定新部门和岗位。");
            switch (action)
            {
                case PersonnelAction.Confirm:
                    if (employee.Status != EmploymentStatus.Probation) throw new ResourceConflictException("只有试用人员可以办理转正。");
                    employee.Status = EmploymentStatus.Active;
                    employee.ConfirmedOn = request.EffectiveDate;
                    summary = "试用转为正式在职";
                    break;
                case PersonnelAction.Transfer:
                case PersonnelAction.Rehire:
                    bool rehire = action == PersonnelAction.Rehire;
                    if ((employee.Status == EmploymentStatus.Departed) != rehire)
                        throw new ResourceConflictException(rehire ? "只有离职档案可以办理返聘。" : "离职人员须先办理返聘。");
                    string department = Text(request.DepartmentId, "新部门", 50, true);
                    string job = Text(request.JobTitle, "新岗位", 120, true);
                    await DemandDepartmentAsync(db, department, actor, token);
                    // A department-scoped manager must be authorized for both sides of a move.
                    office.DemandRecord(new PersonnelEmployee
                    {
                        CompanyScope = employee.CompanyScope,
                        DepartmentId = department,
                        OwnerUserId = employee.OwnerUserId
                    }, actor, Resource, PermissionAction.Transition);
                    office.DemandRecord(new PersonnelEmployee
                    {
                        CompanyScope = employee.CompanyScope,
                        DepartmentId = department,
                        OwnerUserId = employee.OwnerUserId
                    }, actor, Resource, PermissionAction.ViewDetails);
                    if (!rehire && department == employee.DepartmentId && job == employee.JobTitle)
                        throw new ServiceValidationException("请选择不同的部门或岗位。");
                    if (rehire || department != employee.DepartmentId)
                    {
                        if (employee.OwnerUserId.HasValue)
                            await OfficeAccountAccess.LockAsync(db, employee.OwnerUserId.Value, employee.CompanyScope, token);
                        await DemandClearanceAsync(db, employee.OwnerUserId, token, employee.Id);
                    }
                    summary = $"{employee.DepartmentId} / {employee.JobTitle} → {department} / {job}";
                    employee.DepartmentId = department;
                    employee.JobTitle = job;
                    if (rehire)
                    {
                        employee.Status = request.OnProbation ? EmploymentStatus.Probation : EmploymentStatus.Active;
                        employee.HireDate = request.EffectiveDate;
                        employee.ConfirmedOn = request.OnProbation ? null : request.EffectiveDate;
                        employee.DepartedOn = null;
                        employee.ProbationEndsOn = null;
                        employee.ContractEndsOn = null;
                        if (employee.OwnerUserId.HasValue) summary += "；返聘后由系统管理员复核账号权限并启用";
                    }
                    await SynchronizeAccountAsync(db, employee, actor, token);
                    break;
                case PersonnelAction.Depart:
                    if (employee.Status == EmploymentStatus.Departed) throw new ResourceConflictException("该人员已经离职。");
                    if (await db.OrganizationDepartments.AnyAsync(item => item.ManagerEmployeeId == employee.Id, token))
                        throw new ResourceConflictException("该人员仍担任部门负责人，请先在组织架构中调整负责人，再办理离职。");
                    if (employee.OwnerUserId.HasValue)
                    {
                        await OfficeAccountAccess.LockAsync(db, employee.OwnerUserId.Value, employee.CompanyScope, token);
                    }
                    await DemandClearanceAsync(db, employee.OwnerUserId, token, employee.Id);
                    employee.Status = EmploymentStatus.Departed;
                    employee.DepartedOn = request.EffectiveDate;
                    await SynchronizeAccountAsync(db, employee, actor, token);
                    summary = "完成离职交接并归档" + (employee.OwnerUserId.HasValue ? "；关联账号已停用，会话已撤销" : "");
                    break;
                default: throw new ServiceValidationException("未知人事操作。");
            }
            employee.LastEffectiveDate = request.EffectiveDate;
            AddEvent(db, employee, actor, action.ToString(), request.EffectiveDate, summary, note);
            await db.SaveChangesAsync(token);
            return new PersonnelChangeResult(await RecordAsync(db, employee, actor, token),
                action == PersonnelAction.Confirm ? null : employee.OwnerUserId);
        }, cancellationToken);

    public Task<PagedResult<PersonnelAccountRecord>> AccountOptionsAsync(int id, string? keyword, int pageNumber, int pageSize,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Assign, false, async (db, actor, token) =>
        {
            DemandAccountAdministrator(actor);
            var employee = await EmployeeAsync(db, id, actor, PermissionAction.Assign, false, token);
            string search = Text(keyword, "搜索词", 100).ToUpperInvariant();
            var accounts = db.Users.AsNoTracking().Where(item => item.CompanyScope == employee.CompanyScope &&
                item.DepartmentId == employee.DepartmentId && item.IsActive && item.Role != UserRoleCatalog.Admin && item.Id != actor.Id &&
                !db.PersonnelEmployees.Any(person => person.OwnerUserId == item.Id));
            if (search.Length > 0) accounts = accounts.Where(item => item.UsernameNormalized.Contains(search) || item.FullName != null && item.FullName.ToUpper().Contains(search));
            return await PageAsync(accounts.OrderBy(item => item.UsernameNormalized).Select(item => new PersonnelAccountRecord(
                item.Id, item.Username, item.FullName ?? "", item.DepartmentId ?? "", item.IsActive, item.VersionNumber)), pageNumber, pageSize, token);
        }, cancellationToken);

    public Task<PersonnelChangeResult> LinkAccountAsync(int id, PersonnelAccountRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Assign, true, async (db, actor, token) =>
        {
            DemandAccountAdministrator(actor);
            var employee = await EmployeeAsync(db, id, actor, PermissionAction.Assign, true, token);
            Version(request.ExpectedVersion, employee.VersionNumber);
            if (employee.OwnerUserId.HasValue || employee.Status == EmploymentStatus.Departed)
                throw new ResourceConflictException("已关联或已离职的档案不能更换关联账号。");
            var account = await OfficeAccountAccess.LockAsync(db, request.UserId, employee.CompanyScope, token);
            Version(request.ExpectedAccountVersion, account.VersionNumber);
            if (!account.IsActive || account.DepartmentId != employee.DepartmentId || account.Id == actor.Id ||
                BusinessDataAccessScope.CanViewAllBusinessData(account))
                throw new ServiceValidationException("请选择本公司同部门的启用普通账号；管理员账号单独维护。");
            if (await db.PersonnelEmployees.AnyAsync(item => item.OwnerUserId == account.Id, token))
                throw new ResourceConflictException("该账号已关联其他人员档案。");
            employee.OwnerUserId = account.Id;
            await SynchronizeAccountAsync(db, employee, actor, token);
            AddEvent(db, employee, actor, "LinkAccount", office.Clock.Today, $"关联账号：{account.Username}；姓名与组织范围由人员档案维护", "");
            await db.SaveChangesAsync(token);
            return new PersonnelChangeResult(await RecordAsync(db, employee, actor, token), employee.OwnerUserId);
        }, cancellationToken);

    private async Task SynchronizeAccountAsync(AppDbContext db, PersonnelEmployee employee, User actor, CancellationToken token)
    {
        if (!employee.OwnerUserId.HasValue) return;
        var account = await OfficeAccountAccess.LockAsync(db, employee.OwnerUserId.Value, employee.CompanyScope, token);
        if (BusinessDataAccessScope.CanViewAllBusinessData(account))
            throw new PermissionDeniedException("管理员账号须在账号与权限中单独维护。");
        if (account.Id == actor.Id && employee.Status == EmploymentStatus.Departed)
            throw new PermissionDeniedException("不能停用当前登录账号。");
        account.FullName = employee.FullName;
        account.DepartmentId = employee.DepartmentId;
        if (employee.Status == EmploymentStatus.Departed) account.IsActive = false;
        account.VersionNumber++;
        var now = office.Clock.UtcNow;
        await db.ApiUserSessions.Where(item => item.UserId == account.Id && !item.RevokedAt.HasValue)
            .ExecuteUpdateAsync(setter => setter.SetProperty(item => item.RevokedAt, now), token);
    }

    private void DemandAccountAdministrator(User actor)
    {
        if (office.IsLocalRegister)
            throw new PermissionDeniedException("单机行政版按人员档案登记，无需关联员工登录账号。");
        if (!BusinessDataAccessScope.CanViewAllBusinessData(actor))
            throw new PermissionDeniedException("关联登录账号须由系统管理员办理。");
    }
}

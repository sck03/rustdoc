using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class PersonnelService(OfficeServiceContext office) : IPersonnelService
{
    private const string Resource = PermissionResourceCatalog.OfficePeople;

    public Task<PersonnelRecord> CreateAsync(PersonnelCreateRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Create, true, async (db, actor, token) =>
        {
            if (db.Database.IsNpgsql())
            {
                var company = await db.OrganizationCompanies.FromSqlInterpolated(
                    $"SELECT * FROM \"OrganizationCompanies\" WHERE \"Code\" = {actor.CompanyScope} FOR UPDATE").SingleAsync(token);
                if (!company.IsActive) throw new PermissionDeniedException("所属公司已停用。");
            }
            RequestKey(request.RequestKey);
            string number = Text(request.EmployeeNumber, "工号", 40, true);
            if (number.Any(char.IsWhiteSpace)) throw new ServiceValidationException("工号不能包含空白字符。");
            var employee = new PersonnelEmployee
            {
                CompanyScope = actor.CompanyScope!,
                RequestKey = request.RequestKey,
                EmployeeNumber = number,
                DepartmentId = Text(request.DepartmentId, "部门", 50, true),
                JobTitle = Text(request.JobTitle, "岗位", 120, true),
                HireDate = request.HireDate,
                LastEffectiveDate = request.HireDate,
                Status = request.OnProbation ? EmploymentStatus.Probation : EmploymentStatus.Active,
                ConfirmedOn = request.OnProbation ? null : request.HireDate
            };
            ApplyProfile(employee, request.Profile, request.EmploymentType, request.ProbationEndsOn, request.ContractEndsOn);
            ValidateEffectiveDate(employee, request.HireDate);
            await DemandDepartmentAsync(db, employee.DepartmentId, actor, token);
            office.DemandRecord(employee, actor, Resource, PermissionAction.Create);
            office.DemandRecord(employee, actor, Resource, PermissionAction.ViewDetails);
            var previous = await db.PersonnelEmployees.SingleOrDefaultAsync(item => item.CompanyScope == actor.CompanyScope && item.RequestKey == request.RequestKey, token);
            if (previous != null)
            {
                office.DemandRecord(previous, actor, Resource, PermissionAction.Create);
                if (previous.EmployeeNumber != employee.EmployeeNumber || previous.HireDate != employee.HireDate ||
                    previous.DepartmentId != employee.DepartmentId || previous.JobTitle != employee.JobTitle ||
                    previous.Status != employee.Status || Profile(previous) != Profile(employee) ||
                    previous.EmploymentType != employee.EmploymentType || previous.ProbationEndsOn != employee.ProbationEndsOn ||
                    previous.ContractEndsOn != employee.ContractEndsOn)
                    throw new ResourceConflictException("该登记标识已用于其他内容，请刷新后核对人员档案。");
                return await RecordAsync(db, previous, actor, token);
            }
            db.PersonnelEmployees.Add(employee);
            await db.SaveChangesAsync(token);
            AddEvent(db, employee, actor, "Hire", request.HireDate, $"入职登记：{employee.EmployeeNumber} · {employee.JobTitle}", "");
            await db.SaveChangesAsync(token);
            return await RecordAsync(db, employee, actor, token);
        }, cancellationToken);

    public Task<PersonnelChangeResult> UpdateAsync(int id, PersonnelUpdateRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Edit, true, async (db, actor, token) =>
        {
            var employee = await EmployeeAsync(db, id, actor, PermissionAction.Edit, true, token);
            Version(request.ExpectedVersion, employee.VersionNumber);
            if (employee.Status == EmploymentStatus.Departed) throw new ResourceConflictException("离职档案已归档；返聘后可继续维护。");
            var before = Profile(employee);
            var oldType = employee.EmploymentType;
            var oldProbation = employee.ProbationEndsOn;
            var oldContract = employee.ContractEndsOn;
            bool corrected = await CorrectRegistrationAsync(db, employee, actor, request.Registration, token);
            ApplyProfile(employee, request.Profile, request.EmploymentType, request.ProbationEndsOn, request.ContractEndsOn);
            string changes = ProfileChanges(before, Profile(employee));
            if (corrected) changes += "入职登记信息；";
            if (oldType != employee.EmploymentType) changes += "用工类型；";
            if (oldProbation != employee.ProbationEndsOn) changes += "试用截止日期；";
            if (oldContract != employee.ContractEndsOn) changes += "合同截止日期；";
            if (changes.Length > 0)
            {
                if (before.FullName != employee.FullName) await SynchronizeAccountAsync(db, employee, actor, token);
                AddEvent(db, employee, actor, "Edit", office.Clock.Today, "更新：" + changes.TrimEnd('；'), "");
                await db.SaveChangesAsync(token);
            }
            return new PersonnelChangeResult(await RecordAsync(db, employee, actor, token),
                before.FullName != employee.FullName ? employee.OwnerUserId : null);
        }, cancellationToken);

    private void ValidateEffectiveDate(PersonnelEmployee employee, DateOnly date)
    {
        if (date < new DateOnly(1900, 1, 1) || date < employee.LastEffectiveDate || date > office.Clock.Today)
            throw new ServiceValidationException("生效日期不能早于上次任职变动或晚于公司业务日期；本次操作立即生效。");
    }

    private static async Task DemandDepartmentAsync(AppDbContext db, string id, User actor, CancellationToken token)
    {
        var query = db.Database.IsNpgsql()
            ? db.OrganizationDepartments.FromSqlInterpolated($"SELECT * FROM \"OrganizationDepartments\" WHERE \"Code\" = {id} AND \"CompanyCode\" = {actor.CompanyScope} FOR SHARE")
            : db.OrganizationDepartments.Where(item => item.Code == id && item.CompanyCode == actor.CompanyScope);
        var department = await query.SingleOrDefaultAsync(token);
        if (department is not { IsActive: true }) throw new ServiceValidationException("请选择本公司启用的部门。");
    }

    private async Task<PersonnelEmployee> EmployeeAsync(AppDbContext db, int id, User actor, string action, bool write, CancellationToken token)
    {
        var query = write && db.Database.IsNpgsql()
            ? db.PersonnelEmployees.FromSqlInterpolated($"SELECT * FROM \"PersonnelEmployees\" WHERE \"Id\" = {id} AND \"CompanyScope\" = {actor.CompanyScope} FOR UPDATE")
            : db.PersonnelEmployees.Where(item => item.Id == id && item.CompanyScope == actor.CompanyScope);
        var employee = await query.SingleOrDefaultAsync(token) ?? throw new ResourceNotFoundException("人员档案不存在。");
        office.DemandRecord(employee, actor, Resource, action);
        if (write) office.DemandRecord(employee, actor, Resource, PermissionAction.ViewDetails);
        return employee;
    }

    private void AddEvent(AppDbContext db, PersonnelEmployee employee, User actor, string action, DateOnly date, string summary, string note) =>
        db.PersonnelEvents.Add(new PersonnelEvent
        {
            EmployeeId = employee.Id,
            CompanyScope = employee.CompanyScope,
            Action = action,
            EffectiveDate = date,
            ActorUserId = actor.Id,
            ActorName = ActorName(actor),
            Summary = summary,
            Note = note,
            CreatedAt = office.Clock.UtcNow
        });
}

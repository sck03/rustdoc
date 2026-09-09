using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Security;

public sealed partial class OrganizationDirectoryService
{
    public Task<OrganizationDepartmentRecord> SaveDepartmentAsync(OrganizationDepartmentSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandAdministrator();
        ArgumentNullException.ThrowIfNull(request);
        string existingCode = NormalizeOptionalCode(request.ExistingCode);
        string code = NormalizeRequiredCode(request.Code, "部门代码");
        string companyCode = NormalizeRequiredCode(request.CompanyCode, "所属公司");
        string name = NormalizeName(request.Name, "部门名称");
        string? parentCode = string.IsNullOrWhiteSpace(request.ParentCode) ? null : NormalizeRequiredCode(request.ParentCode, "上级部门");
        if (existingCode.Length > 0 && existingCode != code)
            throw new ServiceValidationException("部门代码是稳定授权标识，创建后不能修改。");
        if (existingCode.Length == 0 && request.ExpectedVersion != 0)
            throw new ServiceValidationException("新增部门不能包含已有版本号。");
        return RunDirectoryAsync(async (context, token) =>
        {
            var company = await (context.Database.IsNpgsql()
                ? context.OrganizationCompanies.FromSqlInterpolated($"SELECT * FROM \"OrganizationCompanies\" WHERE \"Code\" = {companyCode} FOR UPDATE")
                : context.OrganizationCompanies.Where(item => item.Code == companyCode)).SingleOrDefaultAsync(token);
            if (company is not { IsActive: true }) throw new ServiceValidationException("所属公司不存在或已停用。");
            OrganizationDepartment entity;
            if (existingCode.Length == 0)
            {
                entity = new OrganizationDepartment { Code = code, CompanyCode = companyCode };
                context.OrganizationDepartments.Add(entity);
            }
            else
            {
                entity = await context.OrganizationDepartments.SingleOrDefaultAsync(item => item.Code == code, token)
                    ?? throw new ResourceNotFoundException("部门目录项不存在。");
                PrepareExpectedVersion(context, entity, request.ExpectedVersion, "部门");
                if (entity.CompanyCode != companyCode)
                    throw new ServiceValidationException("部门所属公司创建后不能修改；跨公司调整请新建部门并按人员流程办理。");
                if (!request.IsActive && (await context.Users.AnyAsync(item => item.DepartmentId == code && item.IsActive, token) ||
                    await context.PersonnelEmployees.AnyAsync(item => item.DepartmentId == code && item.Status != EmploymentStatus.Departed, token)))
                    throw new ResourceConflictException("部门仍有启用账号或在职人员，请先调岗或办理离职后再停用部门。");
            }
            string managerName = "";
            if (request.ManagerEmployeeId.HasValue)
            {
                if (!request.IsActive) throw new ServiceValidationException("停用部门前请取消或调整部门负责人。");
                int managerId = request.ManagerEmployeeId.Value;
                // The employee lock also serializes assignment with departure.
                var manager = await (context.Database.IsNpgsql()
                    ? context.PersonnelEmployees.FromSqlInterpolated($"SELECT * FROM \"PersonnelEmployees\" WHERE \"Id\" = {managerId} AND \"CompanyScope\" = {companyCode} FOR UPDATE")
                    : context.PersonnelEmployees.Where(item => item.Id == managerId && item.CompanyScope == companyCode)).SingleOrDefaultAsync(token);
                if (manager == null || manager.Status == EmploymentStatus.Departed)
                    throw new ServiceValidationException("部门负责人须为本公司在职人员。");
                managerName = manager.FullName;
            }
            entity.Name = name;
            entity.ParentCode = parentCode;
            entity.ManagerEmployeeId = request.ManagerEmployeeId;
            entity.IsActive = request.IsActive;
            var departments = await context.OrganizationDepartments.AsNoTracking().Where(item => item.CompanyCode == companyCode).ToListAsync(token);
            ValidateHierarchy(departments, entity);
            await SaveChangesAsync(context, "部门", token);
            return new OrganizationDepartmentRecord(entity.Code, entity.CompanyCode, entity.Name, entity.IsActive, entity.VersionNumber,
                entity.ParentCode, entity.ManagerEmployeeId, managerName);
        }, true, cancellationToken);
    }

    public Task<PagedResult<OrganizationManagerRecord>> ManagerOptionsAsync(string companyCode, string? keyword,
        int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
        RunDirectoryAsync(async (context, token) =>
        {
            string company = NormalizeRequiredCode(companyCode, "所属公司");
            string search = OfficeServiceContext.Text(keyword, "搜索词", 100).ToUpperInvariant();
            var people = context.PersonnelEmployees.AsNoTracking().Where(item => item.CompanyScope == company && item.Status != EmploymentStatus.Departed);
            if (search.Length > 0) people = people.Where(item => item.FullName.ToUpper().Contains(search) || item.EmployeeNumberNormalized.Contains(search));
            return await OfficeServiceContext.PageAsync(people.OrderBy(item => item.EmployeeNumberNormalized).ThenBy(item => item.Id)
                .Select(item => new OrganizationManagerRecord(item.Id, item.FullName, item.EmployeeNumber, item.Department == null ? item.DepartmentId : item.Department.Name)),
                pageNumber, pageSize, token);
        }, false, cancellationToken);

    private static void ValidateHierarchy(List<OrganizationDepartment> departments, OrganizationDepartment changed)
    {
        var directory = departments.ToDictionary(item => item.Code, StringComparer.Ordinal);
        directory[changed.Code] = changed;
        foreach (var department in directory.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { department.Code };
            var current = department;
            while (current.ParentCode != null)
            {
                if (!directory.TryGetValue(current.ParentCode, out var parent))
                    throw new ServiceValidationException("上级部门必须属于同一公司。");
                if (!visited.Add(parent.Code)) throw new ServiceValidationException("部门不能设为自身或下级部门的子部门。");
                if (visited.Count > 32) throw new ServiceValidationException("组织架构最多支持 32 层部门。");
                if (current.IsActive && !parent.IsActive)
                    throw new ResourceConflictException("启用部门的上级部门须启用；停用前请先调整启用的子部门。");
                current = parent;
            }
        }
    }
}

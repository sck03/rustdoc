using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace ExportDocManager.Services.Security;

public sealed partial class OrganizationDirectoryService
{
    public Task DeleteCompanyAsync(string code, DeleteRecordRequest request, CancellationToken cancellationToken = default) =>
        RunDirectoryAsync(async (db, token) =>
        {
            code = NormalizeRequiredCode(code, "公司代码");
            var company = await db.OrganizationCompanies.SingleOrDefaultAsync(item => item.Code == code, token)
                ?? throw new ResourceNotFoundException("公司不存在。");
            PrepareExpectedVersion(db, company, request.ExpectedVersion, "公司");
            string reason = OfficeServiceContext.Text(request.Reason, "删除原因", 500, true);
            if (await HasOrganizationReferencesAsync(db, code, true, token))
                throw new ResourceConflictException("公司仍有部门、账号、人员或业务记录，不能删除；请保留历史并按需停用。");
            RecordDeletionAudit.Add(db, _currentUserContext.CurrentUser!, nameof(OrganizationCompany), code, reason, TimeProvider.System.GetUtcNow());
            db.OrganizationCompanies.Remove(company);
            await SaveChangesAsync(db, "公司", token);
            return true;
        }, true, cancellationToken);

    public Task DeleteDepartmentAsync(string code, DeleteRecordRequest request, CancellationToken cancellationToken = default) =>
        RunDirectoryAsync(async (db, token) =>
        {
            code = NormalizeRequiredCode(code, "部门代码");
            var department = await db.OrganizationDepartments.SingleOrDefaultAsync(item => item.Code == code, token)
                ?? throw new ResourceNotFoundException("部门不存在。");
            PrepareExpectedVersion(db, department, request.ExpectedVersion, "部门");
            string reason = OfficeServiceContext.Text(request.Reason, "删除原因", 500, true);
            if (await db.OrganizationDepartments.AnyAsync(item => item.ParentCode == code, token) ||
                await HasOrganizationReferencesAsync(db, code, false, token))
                throw new ResourceConflictException("部门仍有下级部门、账号、人员或业务记录，不能删除；请保留历史并按需停用。");
            RecordDeletionAudit.Add(db, _currentUserContext.CurrentUser!, nameof(OrganizationDepartment), code, reason, TimeProvider.System.GetUtcNow());
            db.OrganizationDepartments.Remove(department);
            await SaveChangesAsync(db, "部门", token);
            return true;
        }, true, cancellationToken);

    // Ownership scopes are also stored on business records without navigation FKs.
    // Inspect the EF model so new capability modules cannot leave dangling scopes.
    private static async Task<bool> HasOrganizationReferencesAsync(AppDbContext db, string code, bool company, CancellationToken token)
    {
        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties().Where(item => item.ClrType == typeof(string) &&
                (company ? item.Name is "CompanyScope" or "CompanyCode" : item.Name == "DepartmentId")))
            {
                var query = (Task<bool>)ScopeReferenceQuery.MakeGenericMethod(entity.ClrType)
                    .Invoke(null, [db, property.Name, code, token])!;
                if (await query) return true;
            }
        }
        return false;
    }

    private static readonly MethodInfo ScopeReferenceQuery = typeof(OrganizationDirectoryService)
        .GetMethod(nameof(HasScopeReferenceAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<bool> HasScopeReferenceAsync<TEntity>(AppDbContext db, string property, string code, CancellationToken token)
        where TEntity : class => await db.Set<TEntity>().AnyAsync(entity => EF.Property<string>(entity, property) == code, token);
}

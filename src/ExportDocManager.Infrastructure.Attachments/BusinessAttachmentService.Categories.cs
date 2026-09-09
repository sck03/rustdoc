using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Attachments;

public sealed partial class BusinessAttachmentService
{
    public Task<BusinessAttachmentCategoryCatalog> ListCategoriesAsync(int? invoiceId = null, CancellationToken cancellationToken = default) =>
        RunAsync(false, async (db, actor, token) =>
        {
            string company = invoiceId.HasValue
                ? (await InvoiceAsync(db, invoiceId.Value, actor, PermissionAction.View, false, token)).CompanyScope ?? ""
                : actor.CompanyScope ?? "";
            bool canManage = BusinessDataAccessScope.CanViewAllBusinessData(actor) && runtime.IsPermissionAvailable(Resource, PermissionAction.Manage);
            var items = await db.BusinessAttachmentCategories.AsNoTracking().Where(item => item.CompanyScope == company)
                .OrderBy(item => item.Id).Select(item => new BusinessAttachmentCategoryRecord(item.Id, item.Name, item.VersionNumber,
                    canManage ? db.BusinessAttachments.Count(attachment => attachment.CategoryId == item.Id) : 0)).ToListAsync(token);
            return new BusinessAttachmentCategoryCatalog(company, items, canManage && company.Length > 0);
        }, cancellationToken);

    public Task<BusinessAttachmentCategoryRecord> CreateCategoryAsync(BusinessAttachmentCategoryCreate request, CancellationToken cancellationToken = default) =>
        RunAsync(true, async (db, actor, token) =>
        {
            DemandCategoryAdministrator(actor);
            string company = BusinessAttachmentFilePolicy.Text(request.CompanyScope, "公司代码", 50, true);
            string name = CategoryName(request.Name);
            var organization = await (db.Database.IsNpgsql()
                ? db.OrganizationCompanies.FromSqlInterpolated($"SELECT * FROM \"OrganizationCompanies\" WHERE \"Code\" = {company} FOR UPDATE")
                : db.OrganizationCompanies.Where(item => item.Code == company)).SingleOrDefaultAsync(token);
            if (organization is not { IsActive: true }) throw new ServiceValidationException("所属公司不存在或已停用。");
            if (await db.BusinessAttachmentCategories.CountAsync(item => item.CompanyScope == company, token) >= 100)
                throw new ResourceConflictException("每家公司最多设置 100 个资料分类。");
            var item = new BusinessAttachmentCategory { CompanyScope = company, Name = name };
            db.BusinessAttachmentCategories.Add(item);
            await db.SaveChangesAsync(token);
            return new BusinessAttachmentCategoryRecord(item.Id, item.Name, item.VersionNumber, 0);
        }, cancellationToken);

    public Task<BusinessAttachmentCategoryRecord> UpdateCategoryAsync(int id, BusinessAttachmentCategoryUpdate request, CancellationToken cancellationToken = default) =>
        RunAsync(true, async (db, actor, token) =>
        {
            DemandCategoryAdministrator(actor);
            var item = await CategoryForUpdateAsync(db, id, token);
            Version(request.ExpectedVersion, item.VersionNumber);
            item.Name = CategoryName(request.Name);
            await db.SaveChangesAsync(token);
            return new BusinessAttachmentCategoryRecord(item.Id, item.Name, item.VersionNumber,
                await db.BusinessAttachments.CountAsync(attachment => attachment.CategoryId == id, token));
        }, cancellationToken);

    public Task DeleteCategoryAsync(int id, int expectedVersion, CancellationToken cancellationToken = default) =>
        RunAsync(true, async (db, actor, token) =>
        {
            DemandCategoryAdministrator(actor);
            var item = await CategoryForUpdateAsync(db, id, token);
            Version(expectedVersion, item.VersionNumber);
            if (await db.BusinessAttachments.AnyAsync(attachment => attachment.CategoryId == id, token))
                throw new ResourceConflictException("该分类仍有资料使用（含停用资料），请先修改这些资料的分类再删除。");
            db.BusinessAttachmentCategories.Remove(item);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

    private void DemandCategoryAdministrator(User actor)
    {
        Demand(PermissionAction.Manage, actor);
        if (!BusinessDataAccessScope.CanViewAllBusinessData(actor))
            throw new PermissionDeniedException("共享分类目录由系统管理员维护。");
    }

    private static string CategoryName(string name) => BusinessAttachmentFilePolicy.Text(name, "分类名称", 80, true);

    private static async Task<BusinessAttachmentCategory> CategoryForUpdateAsync(AppDbContext db, int id, CancellationToken token) =>
        await (db.Database.IsNpgsql()
            ? db.BusinessAttachmentCategories.FromSqlInterpolated($"SELECT * FROM \"BusinessAttachmentCategories\" WHERE \"Id\" = {id} FOR UPDATE")
            : db.BusinessAttachmentCategories.Where(item => item.Id == id)).SingleOrDefaultAsync(token)
        ?? throw new ResourceNotFoundException("资料分类不存在。");

    private static async Task DemandCategoryAsync(AppDbContext db, int id, string? company, CancellationToken token)
    {
        // Keep the category alive through the assignment transaction, including concurrent deletion.
        var category = await (db.Database.IsNpgsql()
            ? db.BusinessAttachmentCategories.FromSqlInterpolated($"SELECT * FROM \"BusinessAttachmentCategories\" WHERE \"Id\" = {id} FOR SHARE")
            : db.BusinessAttachmentCategories.Where(item => item.Id == id)).SingleOrDefaultAsync(token);
        if (category == null || category.CompanyScope != company)
            throw new ServiceValidationException("请选择该单据所属公司的有效资料分类。");
    }
}

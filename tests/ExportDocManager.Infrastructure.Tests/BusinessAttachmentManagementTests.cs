using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Attachments;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Infrastructure.Tests.BusinessAttachmentServiceTests;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class BusinessAttachmentManagementTests
{
    [Fact]
    public async Task CategoriesAndMetadata_ShouldUseStableCompanyKeysAndPreserveFileHistory()
    {
        using var db = new SqliteTestDatabase();
        int invoiceId = await SeedAsync(db);
        var service = Service(db);
        var original = await service.UploadAsync(invoiceId, Upload(), Bytes("original"));
        var category = await service.CreateCategoryAsync(new("C1", "cafe\u0301 图纸"));
        Assert.Equal("café 图纸", category.Name);
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.CreateCategoryAsync(new("C1", "CAFÉ 图纸")));
        var metadata = new BusinessAttachmentMetadataUpdate(original.VersionNumber, "修正后的图纸", category.Id, "PO-NEW", "STYLE-NEW", "修正分类与归属信息");
        var edited = await service.EditMetadataAsync(original.Id, metadata);
        Assert.True(edited.VersionNumber > original.VersionNumber);
        Assert.Equal(category.Id, edited.CategoryId);
        Assert.Single((await service.QueryAsync(new(Keyword: "PO-NEW"))).Page.Items);
        Assert.Equal("original", System.Text.Encoding.UTF8.GetString((await service.ReadAsync(original.Id, 1)).Content));
        Assert.Single((await service.GetAsync(original.Id)).Revisions);
        Assert.Equal("Edit", Assert.Single((await service.GetAsync(original.Id)).Events).Action);
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => service.EditMetadataAsync(original.Id, metadata));
        var renamed = await service.UpdateCategoryAsync(category.Id, new(category.VersionNumber, "产品图纸"));
        Assert.Equal("产品图纸", (await service.GetAsync(original.Id)).Attachment.CategoryName);
        await service.UpdateAsync(original.Id, new(edited.VersionNumber, null, true, "停用"));
        await Assert.ThrowsAsync<ResourceConflictException>(() => service.DeleteCategoryAsync(category.Id, renamed.VersionNumber));
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => service.UpdateCategoryAsync(category.Id, new(category.VersionNumber, "过期改名")));
        var unused = await service.CreateCategoryAsync(new("C1", "临时分类"));
        await service.DeleteCategoryAsync(unused.Id, unused.VersionNumber);
        Assert.DoesNotContain((await service.ListCategoriesAsync(invoiceId)).Items, item => item.Id == unused.Id);
    }

    [Fact]
    public async Task Categories_ShouldRejectCrossCompanyAssignmentAndOrdinaryUserMaintenance()
    {
        using var db = new SqliteTestDatabase();
        int invoiceId = await SeedAsync(db);
        using (var context = db.CreateDbContext())
        {
            context.OrganizationCompanies.Add(new OrganizationCompany { Code = "C2", Name = "另一公司" });
            await context.SaveChangesAsync();
        }
        var service = Service(db);
        var other = await service.CreateCategoryAsync(new("C2", "公司专用分类"));
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.UploadAsync(invoiceId, Upload() with { CategoryId = other.Id }, Bytes("original")));
        Assert.DoesNotContain((await service.ListCategoriesAsync(invoiceId)).Items, item => item.Id == other.Id);
        var record = await service.UploadAsync(invoiceId, Upload(), Bytes("original"));
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.EditMetadataAsync(record.Id,
            new(record.VersionNumber, record.Title, other.Id, "", "", "不能跨公司")));
        var user = BusinessFeatureTestRuntime.Admin();
        user.Role = UserRoleCatalog.User;
        user.EffectivePermissionGrants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey("document.invoices", PermissionAction.View)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey("document.invoices", PermissionAction.Manage)] = PermissionDataScope.Company
        };
        var limited = Service(db, user);
        Assert.False((await limited.ListCategoriesAsync()).CanManage);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => limited.CreateCategoryAsync(new("C1", "越权分类")));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => limited.UpdateCategoryAsync(1, new(1, "越权改名")));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => limited.DeleteCategoryAsync(1, 1));
    }

    [Fact]
    public async Task Delete_ShouldRequireSourceManagementScopeAndVersionThenFreeQuotaAndRetainAudit()
    {
        using var db = new SqliteTestDatabase();
        int invoiceId = await SeedAsync(db);
        var service = Service(db);
        var record = await service.UploadAsync(invoiceId, Upload(), Bytes("original"));
        var actor = BusinessFeatureTestRuntime.Admin(8);
        actor.Role = UserRoleCatalog.User;
        actor.EffectivePermissionGrants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey("document.invoices", PermissionAction.View)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey("document.invoices", PermissionAction.Operate)] = PermissionDataScope.Company,
            [PermissionResourceCatalog.CreateGrantKey("document.invoices", PermissionAction.Manage)] = PermissionDataScope.Own
        };
        var limited = Service(db, actor);
        Assert.True((await limited.GetAsync(record.Id)).Attachment.CanEdit);
        Assert.False((await limited.GetAsync(record.Id)).Attachment.CanDelete);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => limited.DeleteAsync(record.Id, new(record.VersionNumber, "越权删除")));
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => service.DeleteAsync(record.Id, new(999, "过期删除")));
        await Assert.ThrowsAsync<ServiceValidationException>(() => service.DeleteAsync(record.Id, new(record.VersionNumber, " ")));
        await service.DeleteAsync(record.Id, new(record.VersionNumber, "误传资料"));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(record.Id));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.ReadAsync(record.Id, 1));
        Assert.Equal(0, (await service.QueryAsync(new(InvoiceId: invoiceId))).UsedBytes);
        using var context = db.CreateDbContext();
        Assert.Empty(context.BusinessAttachmentRevisions);
        Assert.Empty(context.BusinessAttachmentEvents);
        Assert.Contains(context.AuditLogs, item => item.EntityName == nameof(BusinessAttachment) && item.Action == "DeleteReason");
        Assert.DoesNotContain(context.AuditLogs, item => item.NewValues != null && item.NewValues.Contains("误传资料"));
        context.Invoices.Remove(await context.Invoices.SingleAsync());
        await context.SaveChangesAsync();
    }
}

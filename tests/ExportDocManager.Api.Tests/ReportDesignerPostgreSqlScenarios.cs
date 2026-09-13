using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Tests;

internal static class ReportDesignerPostgreSqlScenarios
{
    internal static async Task VerifyAsync(IDbContextFactory<AppDbContext> factory, DatabaseConnectionSettings settings, User admin, int customerId)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "report-designer-postgres", Guid.NewGuid().ToString("N"));
        var paths = new RuntimeAppPathProvider(Path.Combine(root, "app"), Path.Combine(root, "data"));
        var adminScope = new BusinessDataAccessScope(settings, new FixedUserContext(admin));
        var grants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.CrmCustomers, PermissionAction.View)] = PermissionDataScope.All,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.ReportTemplates, PermissionAction.View)] = PermissionDataScope.All,
            [PermissionResourceCatalog.CreateGrantKey(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View)] = PermissionDataScope.All,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.ReportResources, PermissionAction.View)] = PermissionDataScope.All
        };
        var reader = new User { Id = admin.Id + 1000, Username = "scope-reader", Role = UserRoleCatalog.User, EffectivePermissionGrants = grants };
        var readerScope = new BusinessDataAccessScope(settings, new FixedUserContext(reader));
        var crm = new CrmService(factory, adminScope);
        var limitedCrm = new CrmService(factory, readerScope);
        var contact = await crm.SaveContactAsync(new CrmContactSaveRequest(0, customerId, "Protected contact", "", "private@example.test", "", ""));
        Assert.Empty((await limitedCrm.QueryContactsAsync(customerId, 1, 20)).Items);
        Assert.Equal(string.Empty, (await limitedCrm.GetEmailVariableDraftAsync(customerId)).ToAddress);
        string contactGrant = PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.CrmContacts, PermissionAction.View);
        grants[contactGrant] = PermissionDataScope.All;
        Assert.Equal(contact.Id, (await limitedCrm.GetEmailVariableDraftAsync(customerId)).CrmContactId);
        grants.Remove(contactGrant);
        Assert.Null((await limitedCrm.GetEmailVariableDraftAsync(customerId)).CrmContactId);
        try
        {
            var templates = new UserReportTemplateService(factory, adminScope, paths);
            var readerTemplates = new UserReportTemplateService(factory, readerScope, paths);
            var draft = await templates.SaveDraftAsync(new UserReportTemplateDraftRequest(0, "ExportDocument", "PostgreSQL metadata", "<p>Private report body</p>"));
            Assert.Contains((await templates.ListAsync(ReportDocumentType.ExportDocument, pageSize: 1)).Items, item => item.Id == draft.Id);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => readerTemplates.GetAsync(draft.Id));
            var published = await templates.PublishAsync(draft.Id, draft.VersionNumber);
            var shared = await templates.ShareAsync(draft.Id, new UserReportTemplateShareRequest(TemplateShareScopeCatalog.All, published.VersionNumber));
            Assert.Equal(draft.ContentHtml, (await readerTemplates.GetAsync(draft.Id)).ContentHtml);
            var history = await readerTemplates.ListVersionsAsync(draft.Id, pageNumber: 2, pageSize: 1);
            Assert.Equal(3, history.TotalCount);
            Assert.Single(history.Items);
            await Assert.ThrowsAsync<UserReportTemplateConcurrencyException>(() => templates.SaveDraftAsync(
                new UserReportTemplateDraftRequest(draft.Id, draft.ReportType, draft.Name, "<p>Stale overwrite</p>", draft.VersionNumber)));
            Assert.Equal(shared.VersionNumber, (await templates.GetAsync(draft.Id)).VersionNumber);

            var files = new ReportTemplateImageResourceService(paths);
            var resources = new ReportTemplateImageResourceAccessService(factory, adminScope, paths, files);
            await using var input = new MemoryStream(RasterImageFixtures.Read("png"));
            var image = await files.StoreAndCommitAsync(input, "image.png", "image/png", resources.RegisterUploadAsync);
            Assert.True(Assert.Single((await resources.QueryAsync(1, 1)).Items).CanRecycle);
            int reads = 0;
            await resources.ReadManyAsync([image.Id, image.Id], _ => reads++);
            Assert.Equal(1, reads);
            var readerResources = new ReportTemplateImageResourceAccessService(factory, readerScope, paths, files);
            Assert.Empty(await readerResources.GetReadableIdsAsync([image.Id]));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => readerResources.ReadAsync(image.Id));
            Assert.True(await resources.RecycleAsync(image.Id));
        }
        finally { AtomicFileHelper.TryDeleteDirectory(root); }
    }

    private sealed class FixedUserContext(User user) : ICurrentUserContext { public User? CurrentUser => user; }
}

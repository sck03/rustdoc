using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.EmailTemplates;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class EmailTemplateServiceTests
    {
        [Fact]
        public async Task Lifecycle_ShouldSeparateContentSharingAndConcurrency()
        {
            using var factory = new SqliteTestDatabase();
            int sharedId;
            using (var seedContext = factory.CreateDbContext())
            {
                var seededShared = new EmailTemplate
                {
                    OwnerUserId = 8,
                    Name = "团队询价模板",
                    Category = "询价",
                    Subject = "Shared",
                    BodyHtml = "<p>Shared</p>",
                    Status = TemplateLifecycleStatusCatalog.Published,
                    ShareScope = TemplateShareScopeCatalog.All,
                    VersionNumber = 1
                };
                seedContext.EmailTemplates.Add(seededShared);
                seedContext.EmailTemplateVersions.Add(new EmailTemplateVersion
                {
                    Template = seededShared,
                    VersionNumber = 1,
                    ChangeType = "发布",
                    Name = seededShared.Name,
                    Category = seededShared.Category,
                    Subject = seededShared.Subject,
                    BodyHtml = seededShared.BodyHtml,
                    Status = seededShared.Status,
                    ShareScope = seededShared.ShareScope,
                    ChangedBy = "owner"
                });
                await seedContext.SaveChangesAsync();
                sharedId = seededShared.Id;
            }

            var service = CreateService(factory, CreateTemplateUser(7));
            var visible = Assert.Single(await service.ListAsync(null, null, includeArchived: true));
            Assert.Equal(sharedId, visible.Id);
            Assert.Equal(TemplateLifecycleStatusCatalog.Published, visible.Status);
            Assert.Equal(TemplateShareScopeCatalog.All, visible.ShareScope);
            Assert.False(visible.CanEdit);
            Assert.False(Assert.Single(await service.ListVersionsAsync(sharedId)).CanRestore);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.SaveDraftAsync(
                new EmailTemplateDraftRequest(
                    sharedId,
                    visible.Name,
                    visible.Category,
                    "Changed",
                    visible.BodyHtml,
                    visible.VersionNumber)));

            var draft = await service.SaveDraftAsync(new EmailTemplateDraftRequest(
                0,
                "我的询价模板",
                "询价",
                "第一版",
                "<p>第一版</p>"));
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, draft.Status);
            Assert.Equal(TemplateShareScopeCatalog.Private, draft.ShareScope);
            Assert.True(draft.CanEdit);
            Assert.True(draft.CanPublish);

            var published = await service.PublishAsync(draft.Id, draft.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Published, published.Status);
            var shared = await service.ShareAsync(
                published.Id,
                new EmailTemplateShareRequest(TemplateShareScopeCatalog.All, published.VersionNumber));
            Assert.Equal(TemplateShareScopeCatalog.All, shared.ShareScope);
            var disabled = await service.DisableAsync(shared.Id, shared.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Disabled, disabled.Status);
            var restored = await service.RestoreAsync(disabled.Id, disabled.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Published, restored.Status);

            await Assert.ThrowsAsync<BusinessConcurrencyException>(() =>
                service.DisableAsync(restored.Id, disabled.VersionNumber));

            var contentDraft = await service.SaveDraftAsync(new EmailTemplateDraftRequest(
                restored.Id,
                restored.Name,
                restored.Category,
                "第二版",
                "<p>第二版</p>",
                restored.VersionNumber));
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, contentDraft.Status);
            Assert.Equal(TemplateShareScopeCatalog.Private, contentDraft.ShareScope);

            var restoredVersion = await service.RestoreVersionAsync(
                contentDraft.Id,
                1,
                contentDraft.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, restoredVersion.Status);
            Assert.Equal(TemplateShareScopeCatalog.Private, restoredVersion.ShareScope);
            Assert.Equal("第一版", restoredVersion.Subject);

            var archived = await service.ArchiveAsync(restoredVersion.Id, restoredVersion.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Archived, archived.Status);
            Assert.DoesNotContain(
                await service.ListAsync(null, null, includeArchived: false),
                item => item.Id == archived.Id);
            Assert.Equal(2, (await service.ListAsync(null, null, includeArchived: true)).Count);
        }

        [Fact]
        public async Task SaveAndPreview_ShouldSanitizeDangerousEmailHtmlAndKeepBusinessFormatting()
        {
            using var factory = new SqliteTestDatabase();
            var service = CreateService(factory, CreateTemplateUser(7));

            var saved = await service.SaveDraftAsync(new EmailTemplateDraftRequest(
                0,
                "安全模板",
                "通用",
                "Hello {{ContactName}}",
                "<h2 onclick=\"alert(1)\">Hello</h2><script>alert(1)</script>" +
                "<p><a href=\"javascript:alert(1)\">危险链接</a>" +
                "<a href=\"https://example.com/order/{{QuotationNo}}\" target=\"_blank\">安全链接</a></p>"));

            Assert.Contains("<h2>Hello</h2>", saved.BodyHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("script", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("javascript:", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("https://example.com/order/{{QuotationNo}}", saved.BodyHtml, StringComparison.Ordinal);
            Assert.Contains("rel=\"noopener noreferrer\"", saved.BodyHtml, StringComparison.Ordinal);

            var preview = service.Preview(new EmailTemplatePreviewRequest(
                saved.Subject,
                saved.BodyHtml + "<img src=x onerror=alert(1)>",
                new Dictionary<string, string>
                {
                    ["ContactName"] = "<管理员>",
                    ["QuotationNo"] = "QT-001\" onclick=\"alert(1)"
                }));

            Assert.Equal("Hello <管理员>", preview.Subject);
            Assert.DoesNotContain("<img", preview.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onerror", preview.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("QT-001&quot; onclick=&quot;alert(1)", preview.BodyHtml, StringComparison.Ordinal);
        }

        private static EmailTemplateService CreateService(SqliteTestDatabase factory, User user) =>
            new(
                factory,
                new BusinessDataAccessScope(
                    CreatePostgreSqlModeSettings(),
                    new FixedCurrentUserContext(user)));

        private static User CreateTemplateUser(int id)
        {
            var actions = PermissionResourceCatalog.ByKey[PermissionResourceCatalog.EmailTemplates].Actions;
            return new User
            {
                Id = id,
                Username = $"sales-{id}",
                Role = UserRoleCatalog.Sales,
                EffectivePermissionGrants = actions.ToDictionary(
                    action => PermissionResourceCatalog.CreateGrantKey(
                        PermissionResourceCatalog.EmailTemplates,
                        action.Key),
                    action => action.Key == PermissionAction.View
                        ? PermissionDataScope.All
                        : PermissionDataScope.Own,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        private static DatabaseConnectionSettings CreatePostgreSqlModeSettings() => new()
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "127.0.0.1",
            PostgreSqlDatabase = "exportdoc_test",
            PostgreSqlUsername = "test_user"
        };


    }
}

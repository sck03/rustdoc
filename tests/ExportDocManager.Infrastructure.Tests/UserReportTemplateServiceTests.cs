using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class UserReportTemplateServiceTests
    {
        [Fact]
        public async Task Lifecycle_ShouldProtectSharedContentAndRequireExpectedVersion()
        {
            using var factory = new SqliteTestDatabase();
            int sharedId;
            using (var seedContext = factory.CreateDbContext())
            {
                var sharedEntity = new UserReportTemplate
                {
                    OwnerUserId = 8,
                    ReportType = ReportDocumentType.ExportDocument.ToString(),
                    Name = "团队出口发票",
                    ContentHtml = "<html>{{ Invoice.InvoiceNo }}</html>",
                    Status = TemplateLifecycleStatusCatalog.Published,
                    ShareScope = TemplateShareScopeCatalog.All,
                    VersionNumber = 1
                };
                seedContext.UserReportTemplates.Add(sharedEntity);
                seedContext.UserReportTemplateVersions.Add(new UserReportTemplateVersion
                {
                    Template = sharedEntity,
                    VersionNumber = 1,
                    ChangeType = "发布",
                    Name = sharedEntity.Name,
                    ContentHtml = sharedEntity.ContentHtml,
                    Status = sharedEntity.Status,
                    ShareScope = sharedEntity.ShareScope,
                    ChangedBy = "owner"
                });
                await seedContext.SaveChangesAsync();
                sharedId = sharedEntity.Id;
            }

            var service = CreateService(factory, CreateTemplateUser(7));
            var shared = Assert.Single(await service.ListAsync(ReportDocumentType.ExportDocument, true));
            Assert.Equal(sharedId, shared.Id);
            Assert.Equal(TemplateLifecycleStatusCatalog.Published, shared.Status);
            Assert.Equal(TemplateShareScopeCatalog.All, shared.ShareScope);
            Assert.False(shared.CanEdit);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.SaveDraftAsync(
                new UserReportTemplateDraftRequest(
                    sharedId,
                    shared.ReportType,
                    shared.Name,
                    shared.ContentHtml,
                    shared.VersionNumber)));

            var draft = await service.SaveDraftAsync(new UserReportTemplateDraftRequest(
                0,
                ReportDocumentType.ExportDocument.ToString(),
                "我的出口发票",
                shared.ContentHtml));
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, draft.Status);
            Assert.Equal(TemplateShareScopeCatalog.Private, draft.ShareScope);
            Assert.True(draft.CanEdit);
            Assert.True(draft.CanPublish);

            var published = await service.PublishAsync(draft.Id, draft.VersionNumber);
            var teamShared = await service.ShareAsync(
                published.Id,
                new UserReportTemplateShareRequest(TemplateShareScopeCatalog.All, published.VersionNumber));
            Assert.Equal(TemplateShareScopeCatalog.All, teamShared.ShareScope);

            var disabled = await service.DisableAsync(teamShared.Id, teamShared.VersionNumber);
            var restored = await service.RestoreAsync(disabled.Id, disabled.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Published, restored.Status);
            await Assert.ThrowsAsync<UserReportTemplateConcurrencyException>(() =>
                service.ArchiveAsync(restored.Id, disabled.VersionNumber));

            var contentDraft = await service.SaveDraftAsync(new UserReportTemplateDraftRequest(
                restored.Id,
                restored.ReportType,
                restored.Name,
                "<html>V2 {{ Invoice.InvoiceNo }}</html>",
                restored.VersionNumber));
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, contentDraft.Status);
            Assert.Equal(TemplateShareScopeCatalog.Private, contentDraft.ShareScope);

            var restoredVersion = await service.RestoreVersionAsync(
                contentDraft.Id,
                1,
                contentDraft.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, restoredVersion.Status);
            Assert.Equal(shared.ContentHtml, restoredVersion.ContentHtml);
            var archived = await service.ArchiveAsync(restoredVersion.Id, restoredVersion.VersionNumber);
            Assert.Equal(TemplateLifecycleStatusCatalog.Archived, archived.Status);
            Assert.Single(await service.ListAsync(ReportDocumentType.ExportDocument, includeArchived: false));
        }

        [Theory]
        [InlineData("PaymentVoucher", "{{ Invoice.InvoiceNo }}")]
        [InlineData("ExportDocument", "{{ Payment.InvoiceNo }}")]
        [InlineData("PaymentVoucher", "{{ Exporter[\"ExporterNameCN\"] }}")]
        [InlineData("ExportDocument", "{{ Payment[\"InvoiceNo\"] }}")]
        [InlineData("PaymentVoucher", "{{ this[\"Exporter\"] }}")]
        [InlineData("PaymentVoucher", "{{ object.eval \"Exporter.ExporterNameCN\" }}")]
        public async Task SaveDraft_ShouldRejectCrossDomainOrDynamicTemplateFields(
            string reportType,
            string content)
        {
            using var factory = new SqliteTestDatabase();
            var service = CreateService(factory, CreateTemplateUser(7));

            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveDraftAsync(
                new UserReportTemplateDraftRequest(0, reportType, "错误模板", content)));
        }

        [Theory]
        [InlineData("<script>alert('x')</script>")]
        [InlineData("<div onclick=\"alert('x')\">unsafe</div>")]
        [InlineData("<iframe src=\"https://example.com\"></iframe>")]
        [InlineData("<img src=\"https://example.com/pixel.png\">")]
        [InlineData("<style>@import url('https://example.com/style.css');</style>")]
        public async Task SaveDraft_ShouldRejectUnsafeBrowserContent(string unsafeContent)
        {
            using var factory = new SqliteTestDatabase();
            var service = CreateService(factory, CreateTemplateUser(7));

            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveDraftAsync(
                new UserReportTemplateDraftRequest(
                    0,
                    ReportDocumentType.ExportDocument.ToString(),
                    "不安全模板",
                    unsafeContent)));
        }

        [Fact]
        public async Task SharedTemplates_ShouldRespectDepartmentCompanyAndPublishedStatus()
        {
            using var factory = new SqliteTestDatabase();
            using (var seed = factory.CreateDbContext())
            {
                seed.UserReportTemplates.AddRange(
                    Shared("部门模板", TemplateShareScopeCatalog.Department, "sales", "acme"),
                    Shared("公司模板", TemplateShareScopeCatalog.Company, string.Empty, "acme"),
                    Shared("全员模板", TemplateShareScopeCatalog.All, string.Empty, string.Empty),
                    Shared(
                        "停用模板",
                        TemplateShareScopeCatalog.All,
                        string.Empty,
                        string.Empty,
                        TemplateLifecycleStatusCatalog.Disabled));
                await seed.SaveChangesAsync();
            }

            var user = CreateTemplateUser(7, "sales", "acme");
            var service = CreateService(factory, user);
            var visible = await service.ListAsync(ReportDocumentType.ExportDocument);
            Assert.Equal(
                new[] { "全员模板", "公司模板", "部门模板" },
                visible.Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal));

            var otherUser = CreateTemplateUser(6, "finance", "other");
            var otherVisible = await CreateService(factory, otherUser)
                .ListAsync(ReportDocumentType.ExportDocument);
            Assert.Equal(new[] { "全员模板" }, otherVisible.Select(item => item.Name));
        }

        [Fact]
        public async Task Clone_ShouldUseVisibleServerSourceAndKeepDesignPermissionIndependent()
        {
            using var factory = new SqliteTestDatabase();
            int sharedId;
            int privateId;
            using (var seed = factory.CreateDbContext())
            {
                var shared = Shared(
                    "可复制共享模板",
                    TemplateShareScopeCatalog.All,
                    string.Empty,
                    string.Empty);
                shared.ContentHtml = "<html>SERVER SOURCE {{ Invoice.InvoiceNo }}</html>";
                var privateTemplate = Shared(
                    "不可见私有模板",
                    TemplateShareScopeCatalog.Private,
                    "finance",
                    "other",
                    TemplateLifecycleStatusCatalog.Draft);
                seed.UserReportTemplates.AddRange(shared, privateTemplate);
                await seed.SaveChangesAsync();
                sharedId = shared.Id;
                privateId = privateTemplate.Id;
            }

            var cloneOnlyUser = CreateTemplateUser(
                7,
                allowedActions: [PermissionAction.View, PermissionAction.Clone]);
            var service = CreateService(factory, cloneOnlyUser);
            var cloned = await service.CloneAsync(new UserReportTemplateCloneRequest(
                ReportDocumentType.ExportDocument.ToString(),
                "共享模板副本",
                SourceUserTemplateId: sharedId));

            Assert.Equal("<html>SERVER SOURCE {{ Invoice.InvoiceNo }}</html>", cloned.ContentHtml);
            Assert.Equal(TemplateLifecycleStatusCatalog.Draft, cloned.Status);
            Assert.Equal(TemplateShareScopeCatalog.Private, cloned.ShareScope);
            Assert.False(cloned.CanEdit);
            await Assert.ThrowsAsync<PermissionDeniedException>(() => service.SaveDraftAsync(
                new UserReportTemplateDraftRequest(
                    0,
                    ReportDocumentType.ExportDocument.ToString(),
                    "绕过复制",
                    "<html>CLIENT CONTENT</html>")));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.CloneAsync(
                new UserReportTemplateCloneRequest(
                    ReportDocumentType.ExportDocument.ToString(),
                    "越权副本",
                    SourceUserTemplateId: privateId)));
        }

        private static UserReportTemplate Shared(
            string name,
            string shareScope,
            string departmentId,
            string companyScope,
            string status = TemplateLifecycleStatusCatalog.Published) =>
            new()
            {
                OwnerUserId = 8,
                ReportType = ReportDocumentType.ExportDocument.ToString(),
                Name = name,
                ContentHtml = "<html>{{ Invoice.InvoiceNo }}</html>",
                Status = status,
                ShareScope = shareScope,
                DepartmentId = departmentId,
                CompanyScope = companyScope
            };

        private static UserReportTemplateService CreateService(SqliteTestDatabase factory, User user) =>
            new(
                factory,
                new BusinessDataAccessScope(
                    CreatePostgreSqlSettings(),
                    new FixedCurrentUserContext(user)));

        private static User CreateTemplateUser(
            int id,
            string departmentId = "sales",
            string companyScope = "acme",
            IReadOnlyCollection<string>? allowedActions = null)
        {
            var actions = PermissionResourceCatalog.ByKey[PermissionResourceCatalog.ReportTemplates].Actions
                .Where(action => allowedActions == null || allowedActions.Contains(action.Key, StringComparer.Ordinal));
            return new User
            {
                Id = id,
                Username = $"document-{id}",
                Role = UserRoleCatalog.User,
                DepartmentId = departmentId,
                CompanyScope = companyScope,
                EffectivePermissionGrants = actions
                    .Select(action => new KeyValuePair<string, string>(
                        PermissionResourceCatalog.CreateGrantKey(
                            PermissionResourceCatalog.ReportTemplates,
                            action.Key),
                        action.Key == PermissionAction.View
                            ? PermissionDataScope.Department
                            : PermissionDataScope.Own))
                    .Append(new KeyValuePair<string, string>(
                        PermissionResourceCatalog.CreateGrantKey(
                            PermissionModuleCatalog.DocumentInvoices,
                            PermissionAction.View),
                        PermissionDataScope.Own))
                    .Append(new KeyValuePair<string, string>(
                        PermissionResourceCatalog.CreateGrantKey(
                            PermissionModuleCatalog.DocumentPayments,
                            PermissionAction.View),
                        PermissionDataScope.Own))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static DatabaseConnectionSettings CreatePostgreSqlSettings() => new()
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "127.0.0.1",
            PostgreSqlDatabase = "test",
            PostgreSqlUsername = "test"
        };


    }
}

using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class UserReportTemplateServiceTests
    {
        [Fact]
        public async Task RegularUser_ShouldManageOwnTemplateAndReadSharedTemplateOnly()
        {
            using var factory = new TestDbContextFactory();
            int sharedId;
            using (var seedContext = factory.CreateDbContext())
            {
                var sharedEntity = new UserReportTemplate
                {
                    OwnerUserId = 8,
                    ReportType = ReportDocumentType.ExportDocument.ToString(),
                    Name = "团队出口发票",
                    ContentHtml = "<html>{{ Invoice.InvoiceNo }}</html>",
                    IsShared = true,
                    ShareScope = UserReportTemplateShareScope.All
                };
                seedContext.UserReportTemplates.Add(sharedEntity);
                await seedContext.SaveChangesAsync();
                sharedId = sharedEntity.Id;
            }

            var user = new User { Id = 7, Username = "document-user", Role = UserRoleCatalog.User };
            var service = new UserReportTemplateService(
                factory,
                new BusinessDataAccessScope(new DatabaseConnectionSettings(), new FixedCurrentUserContext(user)));

            var shared = Assert.Single(await service.ListAsync(ReportDocumentType.ExportDocument, true));
            Assert.Equal(sharedId, shared.Id);
            Assert.True(shared.IsShared);
            Assert.False(shared.CanEdit);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveAsync(
                new UserReportTemplateSaveRequest(
                    sharedId, shared.ReportType, shared.Name, shared.ContentHtml, true, true, UserReportTemplateShareScope.All)));
            Assert.False(await service.DeleteAsync(sharedId));

            var own = await service.SaveAsync(new UserReportTemplateSaveRequest(
                0,
                ReportDocumentType.ExportDocument.ToString(),
                "我的出口发票",
                shared.ContentHtml,
                true,
                false));
            Assert.True(own.CanEdit);
            Assert.False(own.IsShared);
            Assert.Equal(user.Id, own.OwnerUserId);

            var published = await service.SaveAsync(new UserReportTemplateSaveRequest(
                own.Id, own.ReportType, own.Name, own.ContentHtml, true, true, UserReportTemplateShareScope.All, own.VersionNumber));
            Assert.True(published.IsShared);
            Assert.Equal(2, published.VersionNumber);
            var versions = await service.ListVersionsAsync(own.Id);
            Assert.Equal(new[] { 2, 1 }, versions.Select(item => item.VersionNumber));
            var restored = await service.RestoreVersionAsync(own.Id, 1);
            Assert.Equal(3, restored.VersionNumber);
            Assert.False(restored.IsShared);
            await Assert.ThrowsAsync<UserReportTemplateConcurrencyException>(() => service.SaveAsync(
                new UserReportTemplateSaveRequest(own.Id, own.ReportType, own.Name, own.ContentHtml, true, false, UserReportTemplateShareScope.Private, 1)));
            Assert.True(await service.DeleteAsync(own.Id));
        }

        [Fact]
        public async Task Save_ShouldRejectCrossDomainTemplateFields()
        {
            using var factory = new TestDbContextFactory();
            var user = new User { Id = 7, Username = "document-user", Role = UserRoleCatalog.User };
            var service = new UserReportTemplateService(
                factory,
                new BusinessDataAccessScope(new DatabaseConnectionSettings(), new FixedCurrentUserContext(user)));

            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(
                new UserReportTemplateSaveRequest(
                    0,
                    ReportDocumentType.PaymentVoucher.ToString(),
                    "错误付款模板",
                    "{{ Invoice.InvoiceNo }}",
                    true,
                    false)));

            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(
                new UserReportTemplateSaveRequest(
                    0,
                    ReportDocumentType.ExportDocument.ToString(),
                    "错误出口模板",
                    "{{ Payment.InvoiceNo }}",
                    true,
                    false)));
        }

        [Fact]
        public async Task SharedTemplates_ShouldRespectDepartmentCompanyAndActiveScopes()
        {
            using var factory = new TestDbContextFactory();
            using (var seed = factory.CreateDbContext())
            {
                seed.UserReportTemplates.AddRange(
                    new UserReportTemplate
                    {
                        OwnerUserId = 8, ReportType = "ExportDocument", Name = "部门模板", ContentHtml = "department",
                        IsShared = true, ShareScope = UserReportTemplateShareScope.Department, DepartmentId = "sales", CompanyScope = "acme"
                    },
                    new UserReportTemplate
                    {
                        OwnerUserId = 8, ReportType = "ExportDocument", Name = "公司模板", ContentHtml = "company",
                        IsShared = true, ShareScope = UserReportTemplateShareScope.Company, CompanyScope = "acme"
                    },
                    new UserReportTemplate
                    {
                        OwnerUserId = 8, ReportType = "ExportDocument", Name = "全员模板", ContentHtml = "all",
                        IsShared = true, ShareScope = UserReportTemplateShareScope.All
                    },
                    new UserReportTemplate
                    {
                        OwnerUserId = 8, ReportType = "ExportDocument", Name = "停用模板", ContentHtml = "inactive",
                        IsShared = true, ShareScope = UserReportTemplateShareScope.All, IsActive = false
                    });
                await seed.SaveChangesAsync();
            }

            var user = new User { Id = 7, Username = "sales-user", Role = UserRoleCatalog.User, DepartmentId = "sales", CompanyScope = "acme" };
            var service = new UserReportTemplateService(
                factory,
                new BusinessDataAccessScope(new DatabaseConnectionSettings(), new FixedCurrentUserContext(user)));
            var visible = await service.ListAsync(ReportDocumentType.ExportDocument);
            Assert.Equal(new[] { "部门模板", "公司模板", "全员模板" }, visible.Select(item => item.Name).OrderBy(item => item));

            var otherUser = new User { Id = 6, Username = "other-user", Role = UserRoleCatalog.User, DepartmentId = "finance", CompanyScope = "other" };
            var otherService = new UserReportTemplateService(
                factory,
                new BusinessDataAccessScope(new DatabaseConnectionSettings(), new FixedCurrentUserContext(otherUser)));
            var otherVisible = await otherService.ListAsync(ReportDocumentType.ExportDocument);
            Assert.Equal(new[] { "全员模板" }, otherVisible.Select(item => item.Name));
        }

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User currentUser) => CurrentUser = currentUser;
            public User CurrentUser { get; }
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            public AppDbContext CreateDbContext() => new(_options);
            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(CreateDbContext());

            public void Dispose()
            {
                using var context = CreateDbContext();
                context.Database.EnsureDeleted();
            }
        }
    }
}

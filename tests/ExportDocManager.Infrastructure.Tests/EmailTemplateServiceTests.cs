using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.EmailTemplates;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class EmailTemplateServiceTests
    {
        [Fact]
        public async Task SharedTemplate_WhenPostgreSqlRegularUser_ShouldBeReadableButNotEditable()
        {
            using var factory = new TestDbContextFactory();
            int sharedId;
            using (var seedContext = factory.CreateDbContext())
            {
                var shared = new EmailTemplate
                {
                    OwnerUserId = 8,
                    Name = "团队询价模板",
                    Category = "询价",
                    Subject = "Shared",
                    BodyHtml = "<p>Shared</p>",
                    IsActive = true,
                    IsShared = true
                };
                seedContext.EmailTemplates.Add(shared);
                seedContext.EmailTemplateVersions.Add(new EmailTemplateVersion
                {
                    Template = shared,
                    VersionNumber = 1,
                    ChangeType = "创建",
                    Name = shared.Name,
                    Category = shared.Category,
                    Subject = shared.Subject,
                    BodyHtml = shared.BodyHtml,
                    IsActive = shared.IsActive,
                    IsShared = shared.IsShared,
                    ChangedBy = "owner"
                });
                await seedContext.SaveChangesAsync();
                sharedId = shared.Id;
            }

            var currentUser = new FixedCurrentUserContext(new User { Id = 7, Username = "sales", Role = "Sales" });
            var service = new EmailTemplateService(factory, new BusinessDataAccessScope(CreatePostgreSqlModeSettings(), currentUser));

            var visible = Assert.Single(await service.ListAsync(string.Empty, string.Empty, true));
            Assert.Equal(sharedId, visible.Id);
            Assert.True(visible.IsShared);
            Assert.False(visible.CanEdit);
            var sharedVersion = Assert.Single(await service.ListVersionsAsync(sharedId));
            Assert.False(sharedVersion.CanRestore);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveAsync(
                new EmailTemplateSaveRequest(sharedId, visible.Name, visible.Category, "Changed", visible.BodyHtml, true, true)));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RestoreVersionAsync(sharedId, 1));
            Assert.False(await service.DeleteAsync(sharedId));

            var copy = await service.SaveAsync(new EmailTemplateSaveRequest(
                0, visible.Name, visible.Category, visible.Subject, visible.BodyHtml, true, false));
            Assert.True(copy.CanEdit);
            Assert.False(copy.IsShared);
            Assert.NotEqual(sharedId, copy.Id);
            Assert.Equal(1, copy.VersionNumber);

            var version1 = Assert.Single(await service.ListVersionsAsync(copy.Id));
            Assert.Equal("创建", version1.ChangeType);
            Assert.True(version1.CanRestore);

            var updated = await service.SaveAsync(new EmailTemplateSaveRequest(
                copy.Id, copy.Name, copy.Category, "第二版", "<p>第二版</p>", true, false,
                copy.VersionNumber));
            Assert.Equal(2, updated.VersionNumber);
            Assert.Equal(2, (await service.ListVersionsAsync(copy.Id)).Count);

            var restored = await service.RestoreVersionAsync(copy.Id, 1);
            Assert.Equal(3, restored.VersionNumber);
            Assert.Equal(copy.Subject, restored.Subject);
            var versions = await service.ListVersionsAsync(copy.Id);
            Assert.Equal(new[] { 3, 2, 1 }, versions.Select(item => item.VersionNumber));
            Assert.Equal("恢复 V1", versions[0].ChangeType);

            var unchanged = await service.SaveAsync(new EmailTemplateSaveRequest(
                restored.Id, restored.Name, restored.Category, restored.Subject, restored.BodyHtml,
                restored.IsActive, restored.IsShared, restored.VersionNumber));
            Assert.Equal(3, unchanged.VersionNumber);
            Assert.Equal(3, (await service.ListVersionsAsync(copy.Id)).Count);
        }

        private static DatabaseConnectionSettings CreatePostgreSqlModeSettings() => new()
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "127.0.0.1",
            PostgreSqlDatabase = "exportdoc_test",
            PostgreSqlUsername = "test_user"
        };

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

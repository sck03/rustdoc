using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class ContainerLoadingServiceTests
    {
        [Fact]
        public async Task Projects_WhenPostgreSqlMode_ShouldBeOwnerScoped()
        {
            using var database = new TestDatabase();
            var settings = CreatePostgreSqlModeSettings();
            var aliceService = CreateService(database.Factory, settings, CreateUser(7, "alice", "User"));
            var bobService = CreateService(database.Factory, settings, CreateUser(8, "bob", "User"));
            var adminService = CreateService(database.Factory, settings, CreateUser(1, "admin", "Admin"));
            var project = CreateProject("Alice 方案");

            await aliceService.SaveProjectAsync(project, CreateItems());

            Assert.Equal(7, project.OwnerUserId);
            Assert.Equal(1, project.VersionNumber);
            Assert.Single(await aliceService.GetAllProjectsAsync());
            Assert.Empty(await bobService.GetAllProjectsAsync());
            Assert.Null(await bobService.GetProjectAsync(project.Id));
            Assert.Empty(await bobService.GetProjectItemsAsync(project.Id));

            await bobService.DeleteProjectAsync(project.Id);
            Assert.NotNull(await aliceService.GetProjectAsync(project.Id));
            Assert.Single(await adminService.GetAllProjectsAsync());
        }

        [Fact]
        public async Task SaveProject_WhenVersionIsStale_ShouldRejectOverwrite()
        {
            using var database = new TestDatabase();
            var settings = CreatePostgreSqlModeSettings();
            var service = CreateService(database.Factory, settings, CreateUser(7, "alice", "User"));
            var project = CreateProject("并发方案");
            await service.SaveProjectAsync(project, CreateItems());

            var firstCopy = await service.GetProjectAsync(project.Id);
            var staleCopy = await service.GetProjectAsync(project.Id);
            firstCopy.Name = "第一个会话已保存";
            staleCopy.Name = "旧页面覆盖";

            await service.SaveProjectAsync(firstCopy, CreateItems());
            var exception = await Assert.ThrowsAsync<BusinessConcurrencyException>(
                () => service.SaveProjectAsync(staleCopy, CreateItems()));

            Assert.Contains("其他会话修改", exception.Message, StringComparison.Ordinal);
            var saved = await service.GetProjectAsync(project.Id);
            Assert.Equal("第一个会话已保存", saved.Name);
            Assert.Equal(2, saved.VersionNumber);
        }

        private static ContainerLoadingService CreateService(
            IDbContextFactory<AppDbContext> factory,
            DatabaseConnectionSettings settings,
            User user) =>
            new(factory, new BusinessDataAccessScope(settings, new FixedCurrentUserContext(user)));

        private static User CreateUser(int id, string username, string role) => new()
        {
            Id = id,
            Username = username,
            Role = role,
            DepartmentId = "DOC",
            CompanyScope = "CN"
        };

        private static ContainerProject CreateProject(string name) => new()
        {
            Name = name,
            ContainerType = "20GP",
            ContainerLength = 589,
            ContainerWidth = 235,
            ContainerHeight = 239,
            ContainerMaxVolume = 33.2m,
            ContainerMaxWeight = 28000m,
            AllowRotation = true
        };

        private static List<ContainerProjectItem> CreateItems() =>
        [
            new ContainerProjectItem
            {
                Name = "纸箱",
                Length = 60,
                Width = 40,
                Height = 40,
                Weight = 10,
                Quantity = 20,
                LoadSequence = 1
            }
        ];

        private static DatabaseConnectionSettings CreatePostgreSqlModeSettings() => new()
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "127.0.0.1",
            PostgreSqlDatabase = "exportdoc_test",
            PostgreSqlUsername = "test_user"
        };

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User user) => CurrentUser = user;
            public User CurrentUser { get; }
        }

        private sealed class TestDatabase : IDisposable
        {
            private readonly string _root;

            public TestDatabase()
            {
                _root = Path.Combine(Path.GetTempPath(), $"edm-container-tests-{Guid.NewGuid():N}");
                Directory.CreateDirectory(_root);
                string databasePath = Path.Combine(_root, "test.db");
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(DbHelper.BuildConnectionString(databasePath))
                    .Options;
                Factory = new TestDbContextFactory(options);
                using var context = Factory.CreateDbContext();
                context.Database.EnsureCreated();
            }

            public TestDbContextFactory Factory { get; }

            public void Dispose()
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
        {
            private readonly DbContextOptions<AppDbContext> _options;
            public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
            public AppDbContext CreateDbContext() => new(_options);
            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(CreateDbContext());
        }
    }
}

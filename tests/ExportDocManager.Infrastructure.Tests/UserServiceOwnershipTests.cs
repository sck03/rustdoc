using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class UserServiceOwnershipTests
    {
        [Fact]
        public async Task DeleteUser_WhenAnyOwnedBusinessDataExists_ShouldRequireDeactivation()
        {
            Action<AppDbContext, int>[] seedOwnedRows =
            [
                (context, ownerId) => context.CrmCustomers.Add(new CrmCustomer { OwnerUserId = ownerId, Name = "客户" }),
                (context, ownerId) => context.CrmFollowUps.Add(new CrmFollowUp { OwnerUserId = ownerId, CrmCustomerId = 1, Summary = "跟进" }),
                (context, ownerId) => context.SupplierCompanies.Add(new SupplierCompany { OwnerUserId = ownerId, Name = "供应商" }),
                (context, ownerId) => context.SalesOpportunities.Add(new SalesOpportunity { OwnerUserId = ownerId, Title = "商机" }),
                (context, ownerId) => context.EmailTemplates.Add(new EmailTemplate { OwnerUserId = ownerId, Name = "邮件模板" }),
                (context, ownerId) => context.UserReportTemplates.Add(new UserReportTemplate { OwnerUserId = ownerId, Name = "报表模板" }),
                (context, ownerId) => context.ContainerProjects.Add(new ContainerProject { OwnerUserId = ownerId, Name = "装柜方案" })
            ];

            foreach (var seedOwnedRow in seedOwnedRows)
            {
                using var factory = new TestDbContextFactory();
                var admin = new User { Id = 1, Username = "admin", PasswordHash = "hash", FullName = "Admin", Role = UserRoleCatalog.Admin, IsActive = true };
                var target = new User { Id = 2, Username = "operator", PasswordHash = "hash", FullName = "Operator", Role = UserRoleCatalog.User, IsActive = true };
                using (var context = factory.CreateDbContext())
                {
                    context.Users.AddRange(admin, target);
                    seedOwnedRow(context, target.Id);
                    await context.SaveChangesAsync();
                }

                var service = new UserService(
                    factory,
                    new DatabaseConnectionSettings { Provider = DatabaseConnectionSettings.PostgreSqlProvider },
                    new FixedCurrentUserContext(admin));

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.DeleteUserAsync(target.Id));
                Assert.Contains("停用账号", exception.Message, StringComparison.Ordinal);
            }
        }

        private sealed class FixedCurrentUserContext : ICurrentUserContext
        {
            public FixedCurrentUserContext(User user) => CurrentUser = user;
            public User CurrentUser { get; }
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

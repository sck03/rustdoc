using ExportDocManager.Api.Hosting;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExportDocManager.Api.Tests;

public sealed class ApiReportOutputAccessTests
{
    [Theory]
    [InlineData(ReportDocumentType.ExportDocument, PermissionAction.Preview)]
    [InlineData(ReportDocumentType.ExportDocument, PermissionAction.ExportPdf)]
    [InlineData(ReportDocumentType.ExportDocument, PermissionAction.ExportZip)]
    [InlineData(ReportDocumentType.ExportDocument, PermissionAction.SendEmail)]
    [InlineData(ReportDocumentType.PaymentVoucher, PermissionAction.Preview)]
    [InlineData(ReportDocumentType.PaymentVoucher, PermissionAction.ExportPdf)]
    public async Task Output_ShouldCheckItsOwnScopeForEverySourceAndCurrentPermissions(ReportDocumentType type, string action)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var context = new AppDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            foreach (int id in new[] { 1, 2 })
            {
                context.Invoices.Add(new Invoice { Id = id, InvoiceNo = $"INV-{id}", OwnerUserId = id == 1 ? 7 : 8 });
                context.Payments.Add(new Payment { Id = id, InvoiceNo = $"PAY-{id}", OwnerUserId = id == 1 ? 7 : 8 });
            }
            await context.SaveChangesAsync();
        }
        string outputResource = type == ReportDocumentType.ExportDocument
            ? PermissionResourceCatalog.InvoiceOutput : PermissionResourceCatalog.PaymentOutput;
        string sourceResource = ReportDocumentAccessCatalog.GetSourceResource(type);
        var grants = new Dictionary<string, string>
        {
            [PermissionResourceCatalog.CreateGrantKey(sourceResource, PermissionAction.View)] = PermissionDataScope.All,
            [PermissionResourceCatalog.CreateGrantKey(outputResource, action)] = PermissionDataScope.Own,
            [PermissionResourceCatalog.CreateGrantKey(PermissionResourceCatalog.EmailDelivery, PermissionAction.Send)] = PermissionDataScope.Own
        };
        var user = new User { Id = 7, Role = UserRoleCatalog.User, EffectivePermissionGrants = grants };
        var scope = new BusinessDataAccessScope(new DatabaseConnectionSettings
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "localhost",
            PostgreSqlDatabase = "scope",
            PostgreSqlUsername = "test"
        }, new FixedCurrentUser(user));
        using var services = new ServiceCollection()
            .AddSingleton(scope)
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ContextFactory(options))
            .BuildServiceProvider();

        Task Authorize(params int[] ids) => ApiEndpointRouteBuilderExtensions.DemandReportOutputAccessAsync(
            services, type, ids, action, CancellationToken.None);
        await Authorize(1);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => Authorize(2));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => Authorize(1, 2));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Authorize(99));
        grants.Remove(PermissionResourceCatalog.CreateGrantKey(outputResource, action));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => Authorize(1));
    }

    private sealed class FixedCurrentUser(User user) : ICurrentUserContext
    {
        public User CurrentUser { get; } = user;
    }

    private sealed class ContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}

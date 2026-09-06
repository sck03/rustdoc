using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class DocumentWriteScopeTests
{
    [Fact]
    public async Task InvoiceWrites_WithBroadViewAndOwnWrites_ShouldRejectOtherOwnersBeforeChangingData()
    {
        using var database = new SqliteTestDatabase(new AuditInterceptor());
        using var context = database.CreateDbContext();
        var invoice = new Invoice
        {
            InvoiceNo = "FOREIGN-INVOICE",
            OwnerUserId = 8,
            CompanyScope = "C1",
            DepartmentId = "D1",
            Type = InvoiceTypeCatalog.Actual,
            InvoiceDate = new DateOnly(2026, 9, 6),
            ShipmentDate = new DateOnly(2026, 9, 6)
        };
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        var rowVersion = Assert.IsType<byte[]>(invoice.RowVersion);
        var scope = CreateScope();
        var service = new InvoiceService(database, new ItemService(database), new InvoicePartyResolver(scope), scope);

        Assert.NotNull(await service.GetInvoiceByIdAsync(invoice.Id));
        var saved = await service.SaveInvoiceWithAutoCreationAsync(invoice, [], null, null);
        Assert.False(saved.Success);
        Assert.Equal(SaveFailureKind.Forbidden, saved.FailureKind);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.DeleteInvoiceAsync(invoice.Id));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.CopyInvoiceAsync(invoice.Id, "COPY"));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.CopyInvoiceAsTypeAsync(invoice.Id, InvoiceTypeCatalog.Customs));
        foreach (string target in new[] { InvoiceStatusCatalog.Verified, InvoiceStatusCatalog.Cancelled })
        {
            await Assert.ThrowsAsync<PermissionDeniedException>(() => service.TransitionInvoiceStatusAsync(
                new InvoiceStatusTransitionRequest(invoice.Id, target, rowVersion, "reason")));
        }
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.UnverifyInvoiceAsync(invoice.Id, rowVersion, "reason"));
        var companyScope = CreateScope(PermissionDataScope.Company);
        var companyService = new InvoiceService(database, new ItemService(database), new InvoicePartyResolver(companyScope), companyScope);
        await Assert.ThrowsAsync<InvoiceConflictException>(() => companyService.CopyInvoiceAsync(invoice.Id, invoice.InvoiceNo));
        context.ChangeTracker.Clear();
        var stored = Assert.Single(await context.Invoices.ToListAsync());
        Assert.Equal(8, stored.OwnerUserId);
        Assert.Equal(InvoiceStatusCatalog.Draft, stored.Status);
        Assert.Empty(await context.InvoiceStatusHistories.ToListAsync());
    }

    [Fact]
    public async Task PaymentWrites_ShouldUseWriteScopeAndPreserveStoredOwnership()
    {
        using var database = new SqliteTestDatabase(new AuditInterceptor());
        using var context = database.CreateDbContext();
        var payment = new Payment { OwnerUserId = 8, CompanyScope = "C1", DepartmentId = "D1", InvoiceNo = "PAY" };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        var service = new PaymentService(database, CreateScope());
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.SavePaymentAsync(payment));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.DeletePaymentAsync(payment.Id));

        service = new PaymentService(database, CreateScope(PermissionDataScope.Company));
        payment.OwnerUserId = 7;
        payment.CompanyScope = "FORGED";
        payment.DepartmentId = "FORGED";
        payment.Notes = "permitted edit";
        await service.SavePaymentAsync(payment);
        context.ChangeTracker.Clear();
        var stored = Assert.Single(await context.Payments.ToListAsync());
        Assert.Equal("permitted edit", stored.Notes);
        Assert.Equal(8, stored.OwnerUserId);
        Assert.Equal("C1", stored.CompanyScope);
        Assert.Equal("D1", stored.DepartmentId);
    }

    [Fact]
    public async Task MasterDataWrites_WithBroadViewAndOwnWrites_ShouldRejectOtherOwners()
    {
        using var database = new SqliteTestDatabase(new AuditInterceptor());
        using var context = database.CreateDbContext();
        var customer = new Customer { CustomerNameEN = "Customer", OwnerUserId = 8 };
        var exporter = new Exporter { ExporterNameEN = "Exporter", ExporterNameCN = "出口商", OwnerUserId = 8 };
        var payee = new Payee { Name = "Payee", OwnerUserId = 8 };
        context.AddRange(customer, exporter, payee);
        await context.SaveChangesAsync();
        var scope = CreateScope();
        var repository = new LocalMasterDataReadRepository(database, scope);
        var customers = new CustomerService(database, repository, scope);
        var exporters = new ExporterService(database, repository, scope);
        var payees = new PayeeService(database, repository, scope);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => customers.SaveCustomerAsync(customer));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => customers.DeleteCustomerAsync(customer.Id));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => exporters.SaveExporterAsync(exporter));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => exporters.DeleteExporterAsync(exporter.Id));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => payees.SavePayeeAsync(payee));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => payees.DeletePayeeAsync(payee.Id));
        Assert.Equal(1, await context.Customers.CountAsync());
        Assert.Equal(1, await context.Exporters.CountAsync());
        Assert.Equal(1, await context.Payees.CountAsync());
    }

    [Fact]
    public async Task ContainerWrites_WithBroadViewAndOwnWrites_ShouldRejectOtherOwners()
    {
        using var database = new SqliteTestDatabase(new AuditInterceptor());
        using var context = database.CreateDbContext();
        var project = new ContainerProject { Name = "Foreign", OwnerUserId = 8, VersionNumber = 1 };
        context.ContainerProjects.Add(project);
        await context.SaveChangesAsync();
        var service = new ContainerLoadingService(database, CreateScope());
        Assert.NotNull(await service.GetProjectAsync(project.Id));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.SaveProjectAsync(project,
            [new ContainerProjectItem { Name = "Box", Quantity = 1, Length = 10, Width = 10, Height = 10 }]));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.DeleteProjectAsync(project.Id));
        Assert.Equal(1, await context.ContainerProjects.CountAsync());
        Assert.Empty(await context.ContainerProjectItems.ToListAsync());
    }

    [Fact]
    public async Task QueryExports_ShouldCountAndReadOnlyRowsWithinTheExportScope()
    {
        using var database = new SqliteTestDatabase(new AuditInterceptor());
        using var context = database.CreateDbContext();
        context.Invoices.AddRange(
            new Invoice { InvoiceNo = "OWN", OwnerUserId = 7 },
            new Invoice { InvoiceNo = "OTHER", OwnerUserId = 8 });
        await context.SaveChangesAsync();
        var repository = new LocalSharedReadRepository(database, CreateScope());
        var query = new QueryPageQuery();
        Assert.Equal(2, (await repository.QueryPageAsync(query)).TotalCount);
        Assert.Equal(1, await repository.CountExportAsync(query));
        Assert.Equal("OWN", Assert.Single(await repository.QueryExportBatchAsync(query, 0, 100)).InvoiceNo);
    }

    private static BusinessDataAccessScope CreateScope(string writeScope = PermissionDataScope.Own)
    {
        string[] resources =
        [
            PermissionModuleCatalog.DocumentInvoices, PermissionModuleCatalog.DocumentPayments,
            PermissionModuleCatalog.DocumentMasterData, PermissionModuleCatalog.DocumentContainerPacking,
            PermissionModuleCatalog.DocumentQuery
        ];
        var user = new User
        {
            Id = 7,
            Role = UserRoleCatalog.User,
            CompanyScope = "C1",
            DepartmentId = "D1",
            EffectivePermissionGrants = resources.SelectMany(resource => new[]
            {
                new KeyValuePair<string, string>(PermissionResourceCatalog.CreateGrantKey(resource, PermissionAction.View), PermissionDataScope.All),
                new KeyValuePair<string, string>(PermissionResourceCatalog.CreateGrantKey(resource, PermissionAction.Operate), writeScope),
                new KeyValuePair<string, string>(PermissionResourceCatalog.CreateGrantKey(resource, PermissionAction.Manage), writeScope)
            }).ToDictionary(item => item.Key, item => item.Value)
        };
        return new BusinessDataAccessScope(new DatabaseConnectionSettings
        {
            Provider = DatabaseConnectionSettings.PostgreSqlProvider,
            PostgreSqlHost = "localhost",
            PostgreSqlDatabase = "scope",
            PostgreSqlUsername = "test"
        }, new FixedCurrentUser(user));
    }

    private sealed class FixedCurrentUser(User user) : ICurrentUserContext
    {
        public User CurrentUser { get; } = user;
    }

}

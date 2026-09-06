using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ReportEntityLoaderScopeTests
{
    [Fact]
    public async Task PaymentVoucher_WhenPayeeIsOutsidePaymentScope_ShouldFailClosed()
    {
        await using var factory = new InMemoryTestDatabase();
        int paymentId;
        await using (var context = await factory.CreateDbContextAsync())
        {
            var payee = new Payee
            {
                Name = "Foreign Payee",
                OwnerUserId = 8,
                DepartmentId = "FIN-B",
                CompanyScope = "CN"
            };
            context.Payees.Add(payee);
            await context.SaveChangesAsync();

            var seededPayment = new Payment
            {
                InvoiceNo = "PAY-SCOPE-001",
                PayeeId = payee.Id,
                OwnerUserId = 7,
                DepartmentId = "FIN-A",
                CompanyScope = "CN",
                PaymentDate = new DateOnly(2026, 9, 5),
                ShipmentDate = new DateOnly(2026, 9, 5),
                ReceiptDate = new DateOnly(2026, 9, 5)
            };
            context.Payments.Add(seededPayment);
            await context.SaveChangesAsync();
            paymentId = seededPayment.Id;
        }

        var scope = new BusinessDataAccessScope(
            CreatePostgreSqlSettings(),
            new FixedCurrentUserContext(new User
            {
                Id = 7,
                Username = "finance-a",
                Role = UserRoleCatalog.User,
                DepartmentId = "FIN-A",
                CompanyScope = "CN"
            }));
        var loader = new ReportEntityLoader(factory, scope);
        var payment = Assert.IsType<Payment>(await loader.LoadPaymentAsync(paymentId));

        var error = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            loader.LoadPaymentVoucherEntitiesAsync(payment));

        Assert.Contains("收款对象", error.Message, StringComparison.Ordinal);
    }

    private static DatabaseConnectionSettings CreatePostgreSqlSettings() => new()
    {
        Provider = DatabaseConnectionSettings.PostgreSqlProvider,
        PostgreSqlHost = "127.0.0.1",
        PostgreSqlDatabase = "exportdoc_test",
        PostgreSqlUsername = "test_user"
    };


}

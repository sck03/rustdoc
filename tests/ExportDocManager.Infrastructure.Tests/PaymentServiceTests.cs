using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public class PaymentServiceTests
    {
        [Fact]
        public async Task SavePaymentAsync_ShouldApplyCurrentUserOwnership()
        {
            using var factory = new InMemoryTestDatabase();
            var settings = new DatabaseConnectionSettings();
            var service = new PaymentService(
                factory,
                new BusinessDataAccessScope(
                    settings,
                    new FixedCurrentUserContext(new User
                    {
                        Id = 9,
                        Username = "creator",
                        Role = "User",
                        DepartmentId = "Doc",
                        CompanyScope = "CN"
                    })));

            await service.SavePaymentAsync(CreateValidPayment(" OWN-PAY "));

            using var context = factory.CreateDbContext();
            var payment = await context.Payments.SingleAsync();
            Assert.Equal("OWN-PAY", payment.InvoiceNo);
            Assert.Equal(9, payment.OwnerUserId);
            Assert.Equal("Doc", payment.DepartmentId);
            Assert.Equal("CN", payment.CompanyScope);
        }

        [Fact]
        public async Task PaymentService_WhenPostgreSqlRegularUser_ShouldBlockForeignRows()
        {
            using var factory = new InMemoryTestDatabase();
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.Payments.AddRange(
                    new Payment { InvoiceNo = "OWN-PAY", OwnerUserId = 7, PaymentDate = new DateOnly(2026, 6, 22) },
                    new Payment { InvoiceNo = "FOREIGN-PAY", OwnerUserId = 8, PaymentDate = new DateOnly(2026, 6, 22) });
                await seedContext.SaveChangesAsync();
            }

            var settings = CreatePostgreSqlModeSettings();
            var service = new PaymentService(
                factory,
                new BusinessDataAccessScope(
                    settings,
                    new FixedCurrentUserContext(new User { Id = 7, Username = "operator", Role = "User" })));
            using var readContext = factory.CreateDbContext();
            var foreignPaymentId = await readContext.Payments
                .Where(payment => payment.InvoiceNo == "FOREIGN-PAY")
                .Select(payment => payment.Id)
                .SingleAsync();

            var deleted = await service.DeletePaymentAsync(foreignPaymentId);
            var updateException = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                service.SavePaymentAsync(CreateValidPayment("FOREIGN-EDIT", foreignPaymentId, 8)));

            Assert.False(deleted);
            Assert.Contains("无权限", updateException.ToString());
        }

        [Fact]
        public async Task SavePaymentAsync_ShouldAllowBlankBusinessFieldsAndZeroAmounts()
        {
            using var factory = new InMemoryTestDatabase();
            var service = new PaymentService(factory, TestAccessScope.Create());

            int paymentId = await service.SavePaymentAsync(new Payment());

            using var context = factory.CreateDbContext();
            var saved = await context.Payments.SingleAsync(payment => payment.Id == paymentId);
            Assert.Null(saved.PaymentDate);
            Assert.Equal(string.Empty, saved.PayeeName);
            Assert.Equal(string.Empty, saved.PayerName);
            Assert.Equal(0m, saved.CNYAmount);
        }

        private static Payment CreateValidPayment(string invoiceNo, int id = 0, int? ownerUserId = null)
        {
            return new Payment
            {
                Id = id,
                OwnerUserId = ownerUserId,
                InvoiceNo = invoiceNo,
                PaymentDate = new DateOnly(2026, 6, 22),
                PayeeName = "测试收款方",
                PayerName = "测试付款方",
                CNYAmount = 100m
            };
        }

        private static DatabaseConnectionSettings CreatePostgreSqlModeSettings()
        {
            return new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "127.0.0.1",
                PostgreSqlDatabase = "exportdoc_test",
                PostgreSqlUsername = "test_user"
            };
        }


    }
}

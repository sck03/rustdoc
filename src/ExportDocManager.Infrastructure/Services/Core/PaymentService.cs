using System;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public class PaymentService : IPaymentService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public PaymentService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings)
            : this(contextFactory, databaseSettings, null)
        {
        }

        public PaymentService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<int> SavePaymentAsync(Payment payment)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(payment);
                NormalizePayment(payment);
                _businessDataAccessScope.ApplyOwner(payment);

                using var context = await _contextFactory.CreateDbContextAsync();
                if (payment.Id == 0)
                {
                    context.Payments.Add(payment);
                }
                else
                {
                    if (!await _businessDataAccessScope.CanAccessPaymentAsync(
                            context,
                            payment.Id).ConfigureAwait(false))
                    {
                        throw new UnauthorizedAccessException("无权限修改该付款记录。");
                    }

                    context.Payments.Update(payment);
                }
                await context.SaveChangesAsync();
                return payment.Id;
            }
            catch (Exception ex)
            {
                throw new Exception($"保存付款信息失败: {ex.Message}", ex);
            }
        }

        public async Task<bool> DeletePaymentAsync(int id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var entity = await _businessDataAccessScope
                    .ApplyPaymentScope(context.Payments)
                    .FirstOrDefaultAsync(payment => payment.Id == id);
                if (entity == null)
                {
                    return false;
                }

                context.Payments.Remove(entity);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"删除付款失败: {ex.Message}", ex);
            }
        }

        private static void NormalizePayment(Payment payment)
        {
            payment.InvoiceNo = TextSearchHelper.NormalizeValue(payment.InvoiceNo);
            payment.Department = TextSearchHelper.NormalizeValue(payment.Department);
            payment.Project = TextSearchHelper.NormalizeValue(payment.Project);
            payment.PaymentMethod = TextSearchHelper.NormalizeValue(payment.PaymentMethod);
            payment.PayeeName = TextSearchHelper.NormalizeValue(payment.PayeeName);
            payment.PayerName = TextSearchHelper.NormalizeValue(payment.PayerName);
            payment.BankName = TextSearchHelper.NormalizeValue(payment.BankName);
            payment.AccountNo = TextSearchHelper.NormalizeValue(payment.AccountNo);
            payment.Notes = TextSearchHelper.NormalizeValue(payment.Notes);
            payment.GoodsName = TextSearchHelper.NormalizeValue(payment.GoodsName);
            payment.Quantity = TextSearchHelper.NormalizeValue(payment.Quantity);
            payment.ShipmentCountry = TextSearchHelper.NormalizeValue(payment.ShipmentCountry);
        }
    }
}

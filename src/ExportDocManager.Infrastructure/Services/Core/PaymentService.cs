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
            ArgumentNullException.ThrowIfNull(payment);
            NormalizePayment(payment);
            ValidatePayment(payment);
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
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new BusinessConcurrencyException(
                    "该付款记录已被其他用户修改或删除，请刷新后重试。",
                    exception);
            }
            return payment.Id;
        }

        public async Task<bool> DeletePaymentAsync(int id)
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

        private static void ValidatePayment(Payment payment)
        {
            EnsureOptionalDate(payment.PaymentDate, "付款日期");
            EnsureOptionalDate(payment.ShipmentDate, "出运日期");
            EnsureOptionalDate(payment.ReceiptDate, "收票日期");

            if (payment.PayeeId < 0)
            {
                throw new ArgumentException("支付对象资料编号不能小于 0。");
            }
            EnsureTextLength(payment.InvoiceNo, 100, "发票号");
            EnsureTextLength(payment.Department, 100, "部门");
            EnsureTextLength(payment.PaymentMethod, 100, "付款方式");
            EnsureTextLength(payment.Quantity, 100, "数量");
            EnsureTextLength(payment.ShipmentCountry, 100, "出运国家");
            EnsureTextLength(payment.Project, 200, "项目");
            EnsureTextLength(payment.PayeeName, 200, "收款方");
            EnsureTextLength(payment.PayerName, 200, "付款方");
            EnsureTextLength(payment.BankName, 200, "银行");
            EnsureTextLength(payment.AccountNo, 100, "账号");
            EnsureTextLength(payment.GoodsName, 500, "品名");
            EnsureTextLength(payment.Notes, 2000, "备注");

            var amounts = new (decimal Value, string Label)[]
            {
                (payment.USDAmount, "USD 金额"),
                (payment.CNYAmount, "CNY 金额"),
                (payment.TravelExpense, "差旅费"),
                (payment.BusinessEntertainmentExpense, "业务招待费"),
                (payment.TelephoneExpense, "电话费"),
                (payment.OfficeExpense, "办公费"),
                (payment.RepairExpense, "维修费"),
                (payment.FreightMiscExpense, "运杂费"),
                (payment.InspectionExpense, "商检费"),
                (payment.OtherExpense, "其他费用")
            };
            foreach (var amount in amounts)
            {
                if (amount.Value < 0)
                {
                    throw new ArgumentException($"{amount.Label}不能小于 0。");
                }
            }
        }

        private static void EnsureDate(DateTime value, string label)
        {
            if (value == default || value.Year is < 1900 or > 2100)
            {
                throw new ArgumentException($"{label}必须是 1900 至 2100 年之间的有效日期。");
            }
        }

        private static void EnsureOptionalDate(DateTime? value, string label)
        {
            if (value.HasValue)
            {
                EnsureDate(value.Value, label);
            }
        }

        private static void EnsureTextLength(string value, int maximumLength, string label)
        {
            if ((value?.Length ?? 0) > maximumLength)
            {
                throw new ArgumentException($"{label}不能超过 {maximumLength} 个字符。");
            }
        }
    }
}

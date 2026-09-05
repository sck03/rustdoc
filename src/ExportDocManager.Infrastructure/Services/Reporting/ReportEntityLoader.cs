using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportEntityLoader
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public ReportEntityLoader(
            IDbContextFactory<AppDbContext> contextFactory,
            BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<Invoice?> LoadInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await _accessScope.ApplyInvoiceScope(dbContext.Invoices)
                .AsNoTracking()
                .Include(invoice => invoice.Items)
                .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Payment?> LoadPaymentAsync(int paymentId, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await _accessScope.ApplyPaymentScope(dbContext.Payments)
                .AsNoTracking()
                .FirstOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<(Customer customer, Exporter exporter)> LoadInvoiceEntitiesAsync(
            Invoice invoice,
            bool isPreview,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (!isPreview && (invoice.Items == null || invoice.Items.Count == 0))
            {
                invoice.Items = await dbContext.Items
                    .AsNoTracking()
                    .Where(i => i.InvoiceId == invoice.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (isPreview)
            {
                invoice.Items ??= new List<Item>();
            }

            var customer = await LoadCustomerAsync(dbContext, invoice, cancellationToken).ConfigureAwait(false);
            ApplyCustomerSnapshot(invoice, customer);

            var exporter = await LoadExporterAsync(dbContext, invoice, cancellationToken).ConfigureAwait(false);
            ApplyExporterSnapshot(invoice, exporter);

            return (customer, exporter);
        }

        public async Task<Payee?> LoadPaymentVoucherEntitiesAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (payment.PayeeId <= 0)
            {
                return null;
            }

            var payee = await _accessScope.ApplyPayeeScope(dbContext.Payees.AsNoTracking())
                .FirstOrDefaultAsync(x => x.Id == payment.PayeeId, cancellationToken)
                .ConfigureAwait(false);
            if (payee == null && _accessScope.ShouldFilterBusinessData())
            {
                bool exists = await dbContext.Payees.AsNoTracking()
                    .AnyAsync(x => x.Id == payment.PayeeId, cancellationToken)
                    .ConfigureAwait(false);
                if (exists)
                {
                    throw new PermissionDeniedException("付款报表关联的收款对象不在当前账号的数据范围内。");
                }
            }

            return payee;
        }

        private async Task<Customer> LoadCustomerAsync(
            AppDbContext dbContext,
            Invoice invoice,
            CancellationToken cancellationToken)
        {
            if (invoice.CustomerId <= 0)
            {
                return new Customer();
            }

            var customer = await _accessScope
                .ApplyCustomerScope(dbContext.Customers.AsNoTracking())
                .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken)
                .ConfigureAwait(false);
            if (customer == null && _accessScope.ShouldFilterBusinessData())
            {
                bool exists = await dbContext.Customers.AsNoTracking()
                    .AnyAsync(c => c.Id == invoice.CustomerId, cancellationToken)
                    .ConfigureAwait(false);
                if (exists)
                {
                    throw new PermissionDeniedException("报表关联的客户不在当前账号的数据范围内。");
                }
            }

            return customer ?? new Customer();
        }

        private async Task<Exporter> LoadExporterAsync(
            AppDbContext dbContext,
            Invoice invoice,
            CancellationToken cancellationToken)
        {
            if (invoice.ExporterId <= 0)
            {
                return new Exporter();
            }

            var exporter = await _accessScope
                .ApplyExporterScope(dbContext.Exporters.AsNoTracking())
                .FirstOrDefaultAsync(e => e.Id == invoice.ExporterId, cancellationToken)
                .ConfigureAwait(false);
            if (exporter == null && _accessScope.ShouldFilterBusinessData())
            {
                bool exists = await dbContext.Exporters.AsNoTracking()
                    .AnyAsync(e => e.Id == invoice.ExporterId, cancellationToken)
                    .ConfigureAwait(false);
                if (exists)
                {
                    throw new PermissionDeniedException("报表关联的出口商不在当前账号的数据范围内。");
                }
            }

            return exporter ?? new Exporter();
        }

        private static void ApplyCustomerSnapshot(Invoice invoice, Customer customer)
        {
            if (!string.IsNullOrEmpty(invoice.CustomerNameEN)) customer.CustomerNameEN = invoice.CustomerNameEN;
            if (!string.IsNullOrEmpty(invoice.CustomerAddressEN)) customer.AddressEN = invoice.CustomerAddressEN;
            customer.NotifyPartyMode = invoice.NotifyPartyMode;
            var notifyParty = NotifyPartyModePolicy.ResolveForDocument(
                invoice.NotifyPartyMode,
                invoice.CustomerNameEN,
                invoice.CustomerAddressEN,
                invoice.NotifyPartyName,
                invoice.NotifyPartyAddress);
            customer.NotifyPartyName = notifyParty.Name;
            customer.NotifyPartyAddress = notifyParty.Address;
        }

        private static void ApplyExporterSnapshot(Invoice invoice, Exporter exporter)
        {
            if (!string.IsNullOrEmpty(invoice.ExporterNameEN)) exporter.ExporterNameEN = invoice.ExporterNameEN;
            if (!string.IsNullOrEmpty(invoice.ExporterNameCN)) exporter.ExporterNameCN = invoice.ExporterNameCN;
            if (!string.IsNullOrEmpty(invoice.ExporterAddressEN)) exporter.AddressEN = invoice.ExporterAddressEN;
            if (!string.IsNullOrEmpty(invoice.ExporterAddressCN)) exporter.AddressCN = invoice.ExporterAddressCN;
            if (!string.IsNullOrEmpty(invoice.ExporterCreditCode)) exporter.CreditCode = invoice.ExporterCreditCode;
            if (!string.IsNullOrEmpty(invoice.ExporterCustomsCode)) exporter.CustomsCode = invoice.ExporterCustomsCode;
            if (!string.IsNullOrEmpty(invoice.BankName)) exporter.BankName = invoice.BankName;
            if (!string.IsNullOrEmpty(invoice.BankAccount)) exporter.BankAccount = invoice.BankAccount;
            if (!string.IsNullOrEmpty(invoice.SwiftCode)) exporter.SwiftCode = invoice.SwiftCode;
        }
    }
}

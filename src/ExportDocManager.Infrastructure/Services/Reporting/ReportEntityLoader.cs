using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportEntityLoader
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public ReportEntityLoader(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<Invoice> LoadInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await dbContext.Invoices
                .AsNoTracking()
                .Include(invoice => invoice.Items)
                .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Payment> LoadPaymentAsync(int paymentId, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await dbContext.Payments
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

        public async Task<(Exporter exporter, Payee payee)> LoadPaymentVoucherEntitiesAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var exporter = await dbContext.Exporters.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? new Exporter();

            var payee = payment.PayeeId > 0
                ? await dbContext.Payees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == payment.PayeeId, cancellationToken).ConfigureAwait(false)
                : null;

            return (exporter, payee);
        }

        private static async Task<Customer> LoadCustomerAsync(
            AppDbContext dbContext,
            Invoice invoice,
            CancellationToken cancellationToken)
        {
            var customer = invoice.CustomerId > 0
                ? await dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken).ConfigureAwait(false)
                : null;

            return customer ?? new Customer();
        }

        private static async Task<Exporter> LoadExporterAsync(
            AppDbContext dbContext,
            Invoice invoice,
            CancellationToken cancellationToken)
        {
            var exporter = invoice.ExporterId > 0
                ? await dbContext.Exporters.AsNoTracking().FirstOrDefaultAsync(e => e.Id == invoice.ExporterId, cancellationToken).ConfigureAwait(false)
                : null;

            return exporter ?? new Exporter();
        }

        private static void ApplyCustomerSnapshot(Invoice invoice, Customer customer)
        {
            if (!string.IsNullOrEmpty(invoice.CustomerNameEN)) customer.CustomerNameEN = invoice.CustomerNameEN;
            if (!string.IsNullOrEmpty(invoice.CustomerAddressEN)) customer.AddressEN = invoice.CustomerAddressEN;
            if (!string.IsNullOrEmpty(invoice.NotifyPartyName)) customer.NotifyPartyName = invoice.NotifyPartyName;
            if (!string.IsNullOrEmpty(invoice.NotifyPartyAddress)) customer.NotifyPartyAddress = invoice.NotifyPartyAddress;
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

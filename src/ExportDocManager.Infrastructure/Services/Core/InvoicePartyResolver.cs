using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public interface IInvoicePartyResolver
    {
        Task<int> ResolveCustomerIdAsync(
            AppDbContext context,
            Customer customer,
            string fallbackCustomerName = null,
            CancellationToken cancellationToken = default);

        Task<int> ResolveExporterIdAsync(
            AppDbContext context,
            Exporter exporter,
            string fallbackExporterNameEn = null,
            string fallbackExporterNameCn = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class InvoicePartyResolver : IInvoicePartyResolver
    {
        public async Task<int> ResolveCustomerIdAsync(
            AppDbContext context,
            Customer customer,
            string fallbackCustomerName = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (customer == null)
            {
                return 0;
            }

            if (customer.Id > 0)
            {
                return customer.Id;
            }

            customer.CustomerNameEN = Coalesce(customer.CustomerNameEN, fallbackCustomerName);
            MasterDataNormalization.NormalizeCustomer(customer);

            if (string.IsNullOrWhiteSpace(customer.CustomerNameEN) &&
                string.IsNullOrWhiteSpace(customer.TaxId))
            {
                return 0;
            }

            var existingCustomer = await context.Customers.FirstOrDefaultAsync(
                item => (!string.IsNullOrWhiteSpace(customer.CustomerNameEN) && item.CustomerNameEN == customer.CustomerNameEN) ||
                        (!string.IsNullOrWhiteSpace(customer.TaxId) && item.TaxId == customer.TaxId),
                cancellationToken);

            if (existingCustomer != null)
            {
                customer.Id = existingCustomer.Id;
                return existingCustomer.Id;
            }

            customer.Id = 0;
            customer.RowVersion = null;
            await context.Customers.AddAsync(customer, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return customer.Id;
        }

        public async Task<int> ResolveExporterIdAsync(
            AppDbContext context,
            Exporter exporter,
            string fallbackExporterNameEn = null,
            string fallbackExporterNameCn = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (exporter == null)
            {
                return 0;
            }

            if (exporter.Id > 0)
            {
                return exporter.Id;
            }

            exporter.ExporterNameEN = Coalesce(exporter.ExporterNameEN, fallbackExporterNameEn);
            exporter.ExporterNameCN = Coalesce(exporter.ExporterNameCN, fallbackExporterNameCn);
            MasterDataNormalization.NormalizeExporter(exporter);

            if (string.IsNullOrWhiteSpace(exporter.ExporterNameEN) &&
                string.IsNullOrWhiteSpace(exporter.ExporterNameCN) &&
                string.IsNullOrWhiteSpace(exporter.CreditCode))
            {
                return 0;
            }

            var existingExporter = await context.Exporters.FirstOrDefaultAsync(
                item => (!string.IsNullOrWhiteSpace(exporter.ExporterNameEN) && item.ExporterNameEN == exporter.ExporterNameEN) ||
                        (!string.IsNullOrWhiteSpace(exporter.ExporterNameCN) && item.ExporterNameCN == exporter.ExporterNameCN) ||
                        (!string.IsNullOrWhiteSpace(exporter.CreditCode) && item.CreditCode == exporter.CreditCode),
                cancellationToken);

            if (existingExporter != null)
            {
                exporter.Id = existingExporter.Id;
                return existingExporter.Id;
            }

            exporter.Id = 0;
            exporter.RowVersion = null;
            await context.Exporters.AddAsync(exporter, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return exporter.Id;
        }

        private static string Coalesce(string preferredValue, string fallbackValue)
        {
            return string.IsNullOrWhiteSpace(preferredValue) ? fallbackValue : preferredValue;
        }
    }
}

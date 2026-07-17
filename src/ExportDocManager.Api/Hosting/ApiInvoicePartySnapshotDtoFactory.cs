using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiInvoiceDtoFactory
    {
        public static Customer CreateCustomerForAutoCreation(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            if (invoice.CustomerId > 0 || !HasCustomerSnapshot(invoice))
            {
                return null;
            }

            return new Customer
            {
                CustomerNameEN = invoice.CustomerNameEN,
                AddressEN = invoice.CustomerAddressEN,
                NotifyPartyName = invoice.NotifyPartyName,
                NotifyPartyAddress = invoice.NotifyPartyAddress
            };
        }

        public static Exporter CreateExporterForAutoCreation(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);
            if (invoice.ExporterId > 0 || !HasExporterSnapshot(invoice))
            {
                return null;
            }

            return new Exporter
            {
                ExporterNameEN = invoice.ExporterNameEN,
                ExporterNameCN = invoice.ExporterNameCN,
                AddressEN = invoice.ExporterAddressEN,
                AddressCN = invoice.ExporterAddressCN,
                CreditCode = invoice.ExporterCreditCode,
                CustomsCode = invoice.ExporterCustomsCode,
                BankName = invoice.BankName,
                BankAccount = invoice.BankAccount,
                SwiftCode = invoice.SwiftCode
            };
        }

        public static void PreserveExistingOwnership(Invoice target, Invoice existing)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(existing);

            target.OwnerUserId = existing.OwnerUserId;
            target.DepartmentId = existing.DepartmentId ?? string.Empty;
            target.CompanyScope = existing.CompanyScope ?? string.Empty;
            target.RowVersion ??= existing.RowVersion?.ToArray();
        }

        private static bool HasCustomerSnapshot(Invoice invoice)
        {
            return !string.IsNullOrWhiteSpace(invoice.CustomerNameEN) ||
                   !string.IsNullOrWhiteSpace(invoice.CustomerAddressEN) ||
                   !string.IsNullOrWhiteSpace(invoice.NotifyPartyName) ||
                   !string.IsNullOrWhiteSpace(invoice.NotifyPartyAddress);
        }

        private static bool HasExporterSnapshot(Invoice invoice)
        {
            return !string.IsNullOrWhiteSpace(invoice.ExporterNameEN) ||
                   !string.IsNullOrWhiteSpace(invoice.ExporterNameCN) ||
                   !string.IsNullOrWhiteSpace(invoice.ExporterAddressEN) ||
                   !string.IsNullOrWhiteSpace(invoice.ExporterAddressCN) ||
                   !string.IsNullOrWhiteSpace(invoice.ExporterCreditCode) ||
                   !string.IsNullOrWhiteSpace(invoice.ExporterCustomsCode);
        }
    }
}

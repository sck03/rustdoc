using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public static class MasterDataNormalization
    {
        public static void NormalizeCustomer(Customer customer)
        {
            ArgumentNullException.ThrowIfNull(customer);

            customer.CustomerNameEN = TextSearchHelper.NormalizeValue(customer.CustomerNameEN);
            customer.NotifyPartyName = TextSearchHelper.NormalizeValue(customer.NotifyPartyName);
            customer.AddressEN = TextSearchHelper.NormalizeValue(customer.AddressEN);
            customer.NotifyPartyAddress = TextSearchHelper.NormalizeValue(customer.NotifyPartyAddress);
            customer.ContactPerson = TextSearchHelper.NormalizeValue(customer.ContactPerson);
            customer.Phone = TextSearchHelper.NormalizeValue(customer.Phone);
            customer.Email = TextSearchHelper.NormalizeValue(customer.Email);
            customer.TaxId = TextSearchHelper.NormalizeValue(customer.TaxId);
            customer.Notes = TextSearchHelper.NormalizeValue(customer.Notes);
        }

        public static void NormalizeExporter(Exporter exporter)
        {
            ArgumentNullException.ThrowIfNull(exporter);

            exporter.ExporterNameEN = TextSearchHelper.NormalizeValue(exporter.ExporterNameEN);
            exporter.ExporterNameCN = TextSearchHelper.NormalizeValue(exporter.ExporterNameCN);
            exporter.AddressEN = TextSearchHelper.NormalizeValue(exporter.AddressEN);
            exporter.AddressCN = TextSearchHelper.NormalizeValue(exporter.AddressCN);
            exporter.ContactPerson = TextSearchHelper.NormalizeValue(exporter.ContactPerson);
            exporter.CreditCode = TextSearchHelper.NormalizeValue(exporter.CreditCode);
            exporter.CustomsCode = TextSearchHelper.NormalizeValue(exporter.CustomsCode);
            exporter.Phone = TextSearchHelper.NormalizeValue(exporter.Phone);
            exporter.BankName = TextSearchHelper.NormalizeValue(exporter.BankName);
            exporter.BankAccount = TextSearchHelper.NormalizeValue(exporter.BankAccount);
            exporter.SwiftCode = TextSearchHelper.NormalizeValue(exporter.SwiftCode);
            exporter.Notes = TextSearchHelper.NormalizeValue(exporter.Notes);
            exporter.DocSealPath = TextSearchHelper.NormalizeValue(exporter.DocSealPath);
            exporter.CustomsSealPath = TextSearchHelper.NormalizeValue(exporter.CustomsSealPath);
        }
    }
}

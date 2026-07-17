using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs
{
    public static class QueryResultRowMapper
    {
        public static QueryResultRow FromInvoice(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            return new QueryResultRow
            {
                Id = invoice.Id,
                InvoiceNo = invoice.InvoiceNo ?? string.Empty,
                InvoiceDate = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
                ContractNo = invoice.ContractNo ?? string.Empty,
                CustomerName = invoice.CustomerNameEN ?? string.Empty,
                ExporterName = invoice.ExporterNameEN ?? invoice.ExporterNameCN ?? string.Empty,
                DestinationCountry = invoice.DestinationCountry ?? string.Empty,
                TradeTerms = invoice.TradeTerms ?? string.Empty,
                ShipmentDate = invoice.ShipmentDate == DateTime.MinValue
                    ? string.Empty
                    : invoice.ShipmentDate.ToString("yyyy-MM-dd"),
                TransportMode = invoice.TransportMode ?? string.Empty,
                TotalCartons = invoice.TotalCartons,
                TotalQuantity = invoice.TotalQuantity,
                TotalAmount = invoice.TotalAmount,
                Currency = invoice.Currency ?? string.Empty,
                Type = invoice.Type ?? string.Empty
            };
        }
    }
}

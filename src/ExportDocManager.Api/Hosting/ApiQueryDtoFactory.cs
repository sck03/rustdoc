using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiQueryDtoFactory
    {
        public static ApiPagedResponse<ApiQueryInvoiceRowDto> FromPagedInvoices(PagedResult<Invoice> result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiPagedResponse<ApiQueryInvoiceRowDto>(
                result.Items.Select(FromInvoice).ToList(),
                result.TotalCount,
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage);
        }

        public static ApiQueryInvoiceRowDto FromInvoice(Invoice invoice)
        {
            return FromResultRow(QueryResultRowMapper.FromInvoice(invoice));
        }

        private static ApiQueryInvoiceRowDto FromResultRow(QueryResultRow row)
        {
            return new ApiQueryInvoiceRowDto
            {
                Id = row.Id,
                InvoiceNo = row.InvoiceNo ?? string.Empty,
                InvoiceDate = row.InvoiceDate ?? string.Empty,
                ContractNo = row.ContractNo ?? string.Empty,
                CustomerName = row.CustomerName ?? string.Empty,
                ExporterName = row.ExporterName ?? string.Empty,
                DestinationCountry = row.DestinationCountry ?? string.Empty,
                TradeTerms = row.TradeTerms ?? string.Empty,
                ShipmentDate = row.ShipmentDate ?? string.Empty,
                TransportMode = row.TransportMode ?? string.Empty,
                TotalCartons = row.TotalCartons,
                TotalQuantity = row.TotalQuantity,
                TotalAmount = row.TotalAmount,
                Currency = row.Currency ?? string.Empty,
                Type = row.Type ?? string.Empty
            };
        }
    }
}

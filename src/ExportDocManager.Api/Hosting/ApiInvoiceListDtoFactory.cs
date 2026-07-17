using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiInvoiceDtoFactory
    {
        public static ApiPagedResponse<ApiInvoiceListItemDto> FromPagedInvoices(PagedResult<Invoice> result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiPagedResponse<ApiInvoiceListItemDto>(
                result.Items.Select(FromInvoiceListItem).ToList(),
                result.TotalCount,
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage);
        }

        private static ApiInvoiceListItemDto FromInvoiceListItem(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            return new ApiInvoiceListItemDto(
                invoice.Id,
                invoice.InvoiceNo ?? string.Empty,
                invoice.ContractNo ?? string.Empty,
                invoice.InvoiceDate,
                invoice.CustomerNameEN ?? string.Empty,
                invoice.ExporterNameEN ?? invoice.ExporterNameCN ?? string.Empty,
                invoice.DestinationCountry ?? string.Empty,
                invoice.PortOfLoading ?? string.Empty,
                invoice.PortOfDestination ?? string.Empty,
                invoice.Currency ?? string.Empty,
                invoice.TotalAmount,
                invoice.Type ?? string.Empty,
                invoice.Status ?? string.Empty);
        }
    }
}

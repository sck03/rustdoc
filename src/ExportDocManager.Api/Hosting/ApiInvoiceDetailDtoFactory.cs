using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiInvoiceDtoFactory
    {
        public static ApiInvoiceDetailDto FromInvoiceDetail(Invoice invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            return new ApiInvoiceDetailDto
            {
                Id = invoice.Id,
                OwnerUserId = invoice.OwnerUserId,
                DepartmentId = invoice.DepartmentId ?? string.Empty,
                CompanyScope = invoice.CompanyScope ?? string.Empty,
                InvoiceNo = invoice.InvoiceNo ?? string.Empty,
                ContractNo = invoice.ContractNo ?? string.Empty,
                InvoiceDate = invoice.InvoiceDate,
                LetterOfCreditNo = invoice.LetterOfCreditNo ?? string.Empty,
                LetterOfCreditSourcePath = invoice.LetterOfCreditSourcePath ?? string.Empty,
                LetterOfCreditContent = invoice.LetterOfCreditContent ?? string.Empty,
                IssuingBank = invoice.IssuingBank ?? string.Empty,
                CustomsBrokerName = invoice.CustomsBrokerName ?? string.Empty,
                CustomsBrokerCode = invoice.CustomsBrokerCode ?? string.Empty,
                Spare1 = invoice.Spare1 ?? string.Empty,
                Spare2 = invoice.Spare2 ?? string.Empty,
                Spare3 = invoice.Spare3 ?? string.Empty,
                CustomFieldsJson = invoice.CustomFieldsJson ?? string.Empty,
                PaymentTerms = invoice.PaymentTerms ?? string.Empty,
                PortOfLoading = invoice.PortOfLoading ?? string.Empty,
                PortOfDestination = invoice.PortOfDestination ?? string.Empty,
                DestinationCountry = invoice.DestinationCountry ?? string.Empty,
                ShippingMarks = invoice.ShippingMarks ?? string.Empty,
                ShippingMarksType = invoice.ShippingMarksType ?? string.Empty,
                ShippingMarksImage = invoice.ShippingMarksImage ?? string.Empty,
                TradeTerms = invoice.TradeTerms ?? string.Empty,
                TransportMode = invoice.TransportMode ?? string.Empty,
                ShipmentDate = invoice.ShipmentDate,
                ExporterId = invoice.ExporterId,
                CustomerId = invoice.CustomerId,
                TotalCartons = invoice.TotalCartons,
                TotalQuantity = invoice.TotalQuantity,
                TotalGrossWeight = invoice.TotalGrossWeight,
                TotalNetWeight = invoice.TotalNetWeight,
                TotalVolume = invoice.TotalVolume,
                TotalAmount = invoice.TotalAmount,
                TotalPurchaseAmount = invoice.TotalPurchaseAmount,
                TotalTaxRefundAmount = invoice.TotalTaxRefundAmount,
                TotalProfit = invoice.TotalProfit,
                Currency = invoice.Currency ?? string.Empty,
                SpecialTerms = invoice.SpecialTerms ?? string.Empty,
                Type = invoice.Type ?? string.Empty,
                SupervisionMode = invoice.SupervisionMode ?? string.Empty,
                CustomerNameEN = invoice.CustomerNameEN ?? string.Empty,
                CustomerAddressEN = invoice.CustomerAddressEN ?? string.Empty,
                NotifyPartyName = invoice.NotifyPartyName ?? string.Empty,
                NotifyPartyAddress = invoice.NotifyPartyAddress ?? string.Empty,
                ExporterNameEN = invoice.ExporterNameEN ?? string.Empty,
                ExporterNameCN = invoice.ExporterNameCN ?? string.Empty,
                ExporterAddressEN = invoice.ExporterAddressEN ?? string.Empty,
                ExporterAddressCN = invoice.ExporterAddressCN ?? string.Empty,
                ExporterCreditCode = invoice.ExporterCreditCode ?? string.Empty,
                ExporterCustomsCode = invoice.ExporterCustomsCode ?? string.Empty,
                BankName = invoice.BankName ?? string.Empty,
                BankAccount = invoice.BankAccount ?? string.Empty,
                SwiftCode = invoice.SwiftCode ?? string.Empty,
                ExchangeRate = invoice.ExchangeRate,
                Status = invoice.Status ?? string.Empty,
                RowVersion = invoice.RowVersion == null || invoice.RowVersion.Length == 0
                    ? string.Empty
                    : Convert.ToBase64String(invoice.RowVersion),
                Items = invoice.Items?.Select(FromInvoiceItem).ToList() ?? new List<ApiInvoiceItemDto>()
            };
        }

        private static ApiInvoiceItemDto FromInvoiceItem(Item item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new ApiInvoiceItemDto
            {
                Id = item.Id,
                InvoiceId = item.InvoiceId,
                PoNumber = item.PoNumber ?? string.Empty,
                StyleNo = item.StyleNo ?? string.Empty,
                StyleName = item.StyleName ?? string.Empty,
                FabricComposition = item.FabricComposition ?? string.Empty,
                StyleNameCN = item.StyleNameCN ?? string.Empty,
                Brand = item.Brand ?? string.Empty,
                HSCode = item.HSCode ?? string.Empty,
                Origin = item.Origin ?? string.Empty,
                Quantity = item.Quantity,
                UnitEN = item.UnitEN ?? string.Empty,
                UnitCN = item.UnitCN ?? string.Empty,
                PcsPerCtn = item.PcsPerCtn,
                Cartons = item.Cartons,
                CtnUnitEN = item.CtnUnitEN ?? string.Empty,
                CtnUnitCN = item.CtnUnitCN ?? string.Empty,
                Length = item.Length,
                Width = item.Width,
                Height = item.Height,
                Volume = item.Volume,
                GWPerCtn = item.GWPerCtn,
                NWPerCtn = item.NWPerCtn,
                GWTotal = item.GWTotal,
                NWTotal = item.NWTotal,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalPrice,
                PurchasePrice = item.PurchasePrice,
                PurchaseTotal = item.PurchaseTotal,
                TaxRebateRate = item.TaxRebateRate,
                TaxRefundAmount = item.TaxRefundAmount,
                Spare1 = item.Spare1 ?? string.Empty,
                Spare2 = item.Spare2 ?? string.Empty,
                Spare3 = item.Spare3 ?? string.Empty,
                CustomFieldsJson = item.CustomFieldsJson ?? string.Empty
            };
        }
    }
}

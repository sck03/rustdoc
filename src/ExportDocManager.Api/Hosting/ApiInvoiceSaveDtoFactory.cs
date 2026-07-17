using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiInvoiceDtoFactory
    {
        public static Invoice ToInvoiceForSave(ApiInvoiceDetailDto request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new Invoice
            {
                Id = request.Id,
                InvoiceNo = request.InvoiceNo,
                ContractNo = request.ContractNo,
                InvoiceDate = request.InvoiceDate,
                LetterOfCreditNo = request.LetterOfCreditNo,
                LetterOfCreditSourcePath = request.LetterOfCreditSourcePath,
                LetterOfCreditContent = request.LetterOfCreditContent,
                IssuingBank = request.IssuingBank,
                CustomsBrokerName = request.CustomsBrokerName,
                CustomsBrokerCode = request.CustomsBrokerCode,
                Spare1 = request.Spare1,
                Spare2 = request.Spare2,
                Spare3 = request.Spare3,
                CustomFieldsJson = request.CustomFieldsJson,
                PaymentTerms = request.PaymentTerms,
                PortOfLoading = request.PortOfLoading,
                PortOfDestination = request.PortOfDestination,
                DestinationCountry = request.DestinationCountry,
                ShippingMarks = request.ShippingMarks,
                ShippingMarksType = string.IsNullOrWhiteSpace(request.ShippingMarksType)
                    ? "Text"
                    : request.ShippingMarksType,
                ShippingMarksImage = request.ShippingMarksImage,
                TradeTerms = request.TradeTerms,
                TransportMode = request.TransportMode,
                ShipmentDate = request.ShipmentDate,
                ExporterId = request.ExporterId,
                CustomerId = request.CustomerId,
                TotalCartons = request.TotalCartons,
                TotalQuantity = request.TotalQuantity,
                TotalGrossWeight = request.TotalGrossWeight,
                TotalNetWeight = request.TotalNetWeight,
                TotalVolume = request.TotalVolume,
                TotalAmount = request.TotalAmount,
                TotalPurchaseAmount = request.TotalPurchaseAmount,
                TotalTaxRefundAmount = request.TotalTaxRefundAmount,
                TotalProfit = request.TotalProfit,
                Currency = request.Currency,
                SpecialTerms = request.SpecialTerms,
                Type = NormalizeInvoiceType(request.Type),
                SupervisionMode = request.SupervisionMode,
                CustomerNameEN = request.CustomerNameEN,
                CustomerAddressEN = request.CustomerAddressEN,
                NotifyPartyName = request.NotifyPartyName,
                NotifyPartyAddress = request.NotifyPartyAddress,
                ExporterNameEN = request.ExporterNameEN,
                ExporterNameCN = request.ExporterNameCN,
                ExporterAddressEN = request.ExporterAddressEN,
                ExporterAddressCN = request.ExporterAddressCN,
                ExporterCreditCode = request.ExporterCreditCode,
                ExporterCustomsCode = request.ExporterCustomsCode,
                BankName = request.BankName,
                BankAccount = request.BankAccount,
                SwiftCode = request.SwiftCode,
                ExchangeRate = request.ExchangeRate,
                Status = string.IsNullOrWhiteSpace(request.Status)
                    ? InvoiceStatusCatalog.Draft
                    : request.Status,
                RowVersion = DecodeRowVersion(request.RowVersion),
                Items = request.Items?.Select(ToInvoiceItemForSave).ToList() ?? new List<Item>()
            };
        }

        private static Item ToInvoiceItemForSave(ApiInvoiceItemDto item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new Item
            {
                Id = item.Id,
                InvoiceId = item.InvoiceId,
                PoNumber = item.PoNumber,
                StyleNo = item.StyleNo,
                StyleName = item.StyleName,
                FabricComposition = item.FabricComposition,
                StyleNameCN = item.StyleNameCN,
                Brand = item.Brand,
                HSCode = item.HSCode,
                Origin = item.Origin,
                Quantity = item.Quantity,
                UnitEN = item.UnitEN,
                UnitCN = item.UnitCN,
                PcsPerCtn = item.PcsPerCtn,
                Cartons = item.Cartons,
                CtnUnitEN = item.CtnUnitEN,
                CtnUnitCN = item.CtnUnitCN,
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
                Spare1 = item.Spare1,
                Spare2 = item.Spare2,
                Spare3 = item.Spare3,
                CustomFieldsJson = item.CustomFieldsJson
            };
        }
    }
}

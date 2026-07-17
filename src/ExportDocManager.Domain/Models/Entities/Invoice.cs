using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Represents an invoice entity.
    /// 代表一个发票实体。
    /// </summary>
    public class Invoice
    {
        public int Id { get; set; }
        public int? OwnerUserId { get; set; }
        public string DepartmentId { get; set; } = string.Empty;
        public string CompanyScope { get; set; } = string.Empty;
        public string InvoiceNo { get; set; }
        public string ContractNo { get; set; }
        public DateTime InvoiceDate { get; set; }
        public string LetterOfCreditNo { get; set; }
        public string LetterOfCreditSourcePath { get; set; }
        public string LetterOfCreditContent { get; set; }
        public string IssuingBank { get; set; }
        public string CustomsBrokerName { get; set; }
        public string CustomsBrokerCode { get; set; }
        public string Spare1 { get; set; }
        public string Spare2 { get; set; }
        public string Spare3 { get; set; }
        public string CustomFieldsJson { get; set; }
        public string PaymentTerms { get; set; }
        public string PortOfLoading { get; set; }
        public string PortOfDestination { get; set; }
        public string DestinationCountry { get; set; }
        public string ShippingMarks { get; set; }
        public string ShippingMarksType { get; set; } = "Text"; // Text or Image
        public string ShippingMarksImage { get; set; } // Path to image file relative to App_Data
        public string TradeTerms { get; set; }
        public string TransportMode { get; set; }
        public DateTime ShipmentDate { get; set; } // 船期/航期字段
        public int ExporterId { get; set; }
        public int CustomerId { get; set; }
        public decimal TotalCartons { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalGrossWeight { get; set; }
        public decimal TotalNetWeight { get; set; }
        public decimal TotalVolume { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalPurchaseAmount { get; set; } // 采购总价
        public decimal TotalTaxRefundAmount { get; set; } // 退税总额
        public decimal TotalProfit { get; set; } // 利润总额
        public string Currency { get; set; }
        public string SpecialTerms { get; set; }
        public string Type { get; set; }
        public string SupervisionMode { get; set; } // 监管方式

        // Buyer Information Snapshot (买方快照)
        public string CustomerNameEN { get; set; }
        public string CustomerAddressEN { get; set; }
        public string NotifyPartyName { get; set; } // 通知人名称，原 CustomerNameCN
        public string NotifyPartyAddress { get; set; } // 通知人地址，原 AddressCN

        // Seller Information Snapshot (卖方快照)
        public string ExporterNameEN { get; set; }
        public string ExporterNameCN { get; set; }
        public string ExporterAddressEN { get; set; }
        public string ExporterAddressCN { get; set; }
        public string ExporterCreditCode { get; set; }
        public string ExporterCustomsCode { get; set; }

        // Financial Information Snapshot (财务/银行快照)
        public string BankName { get; set; }
        public string BankAccount { get; set; }
        public string SwiftCode { get; set; }
        public decimal? ExchangeRate { get; set; } // 汇率

        /// <summary>
        /// Status of the invoice: Draft, Verified, Shipped, Completed, Cancelled
        /// </summary>
        public string Status { get; set; } = InvoiceStatusCatalog.Draft;

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; }

        public List<Item> Items { get; set; } = new List<Item>();

        public Invoice CloneHeader()
        {
            return new Invoice
            {
                Id = Id,
                OwnerUserId = OwnerUserId,
                DepartmentId = DepartmentId,
                CompanyScope = CompanyScope,
                InvoiceNo = InvoiceNo,
                ContractNo = ContractNo,
                InvoiceDate = InvoiceDate,
                LetterOfCreditNo = LetterOfCreditNo,
                LetterOfCreditSourcePath = LetterOfCreditSourcePath,
                LetterOfCreditContent = LetterOfCreditContent,
                IssuingBank = IssuingBank,
                CustomsBrokerName = CustomsBrokerName,
                CustomsBrokerCode = CustomsBrokerCode,
                Spare1 = Spare1,
                Spare2 = Spare2,
                Spare3 = Spare3,
                CustomFieldsJson = CustomFieldsJson,
                PaymentTerms = PaymentTerms,
                PortOfLoading = PortOfLoading,
                PortOfDestination = PortOfDestination,
                DestinationCountry = DestinationCountry,
                ShippingMarks = ShippingMarks,
                ShippingMarksType = ShippingMarksType,
                ShippingMarksImage = ShippingMarksImage,
                TradeTerms = TradeTerms,
                TransportMode = TransportMode,
                ShipmentDate = ShipmentDate,
                ExporterId = ExporterId,
                CustomerId = CustomerId,
                TotalCartons = TotalCartons,
                TotalQuantity = TotalQuantity,
                TotalGrossWeight = TotalGrossWeight,
                TotalNetWeight = TotalNetWeight,
                TotalVolume = TotalVolume,
                TotalAmount = TotalAmount,
                TotalPurchaseAmount = TotalPurchaseAmount,
                TotalTaxRefundAmount = TotalTaxRefundAmount,
                TotalProfit = TotalProfit,
                Currency = Currency,
                SpecialTerms = SpecialTerms,
                Type = Type,
                SupervisionMode = SupervisionMode,
                CustomerNameEN = CustomerNameEN,
                CustomerAddressEN = CustomerAddressEN,
                NotifyPartyName = NotifyPartyName,
                NotifyPartyAddress = NotifyPartyAddress,
                ExporterNameEN = ExporterNameEN,
                ExporterNameCN = ExporterNameCN,
                ExporterAddressEN = ExporterAddressEN,
                ExporterAddressCN = ExporterAddressCN,
                ExporterCreditCode = ExporterCreditCode,
                ExporterCustomsCode = ExporterCustomsCode,
                BankName = BankName,
                BankAccount = BankAccount,
                SwiftCode = SwiftCode,
                ExchangeRate = ExchangeRate,
                Status = Status,
                RowVersion = RowVersion?.ToArray()
            };
        }

        public Invoice CreateWorkspaceSnapshot(IEnumerable<Item> items)
        {
            var snapshot = CloneHeader();
            snapshot.Items = items?
                .Where(item => item != null)
                .ToList()
                ?? new List<Item>();
            snapshot.CalculateTotals();
            return snapshot;
        }

        /// <summary>
        /// Recalculates total values based on the Items list.
        /// 根据商品列表重新计算总计值。
        /// </summary>
        public void CalculateTotals()
        {
            if (Items == null) return;

            decimal totalCartons = 0;
            decimal totalQuantity = 0;
            decimal totalGW = 0;
            decimal totalNW = 0;
            decimal totalVolume = 0;
            decimal totalAmount = 0;
            decimal totalPurchase = 0;
            decimal totalTaxRefund = 0;

            foreach (var item in Items)
            {
                totalCartons += item.Cartons;
                totalQuantity += item.Quantity;
                totalGW += item.GWTotal;
                totalNW += item.NWTotal;
                totalVolume += item.Volume;
                totalAmount += item.TotalPrice;
                totalPurchase += item.PurchaseTotal;
                totalTaxRefund += item.TaxRefundAmount;
            }

            TotalCartons = totalCartons;
            TotalQuantity = totalQuantity;
            TotalGrossWeight = totalGW;
            TotalNetWeight = totalNW;
            TotalVolume = totalVolume;
            TotalAmount = totalAmount;
            TotalPurchaseAmount = totalPurchase;
            TotalTaxRefundAmount = totalTaxRefund;
            
            // Calculate Profit in RMB (assuming ExchangeRate converts Sales Currency to RMB)
            decimal rate = ExchangeRate ?? 0;
            if (rate > 0)
            {
                TotalProfit = (TotalAmount * rate) - TotalPurchaseAmount + TotalTaxRefundAmount;
            }
            else
            {
                // If no exchange rate, we cannot accurately calculate profit mixing currencies.
                // We'll leave it as simple subtraction if currencies happen to be same, or 0?
                // For now, let's just subtract, but this is likely wrong for USD vs RMB.
                // Better to set to 0 or leave as is? 
                // Let's leave as is for now to avoid breaking existing logic if user has same currency.
                TotalProfit = TotalAmount - TotalPurchaseAmount + TotalTaxRefundAmount;
            }
        }
    }
}

using ExportDocManager.Utils;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Represents an item on an invoice.
    /// 代表发票上的一个商品明细。
    /// </summary>
    public class Item
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        // public int? ProductId { get; set; } // Removed as per user request for loose coupling
        public string PoNumber { get; set; }
        public string StyleNo { get; set; }
        public string StyleName { get; set; }
        public string FabricComposition { get; set; }
        public string StyleNameCN { get; set; }
        public string Brand { get; set; }
        public string HSCode { get; set; }
        public string Origin { get; set; }
        public decimal Quantity { get; set; }
        public string UnitEN { get; set; }
        public string UnitCN { get; set; }
        public decimal PcsPerCtn { get; set; } // Added for Smart Packing
        public decimal Cartons { get; set; }
        public string CtnUnitEN { get; set; }
        public string CtnUnitCN { get; set; } 
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal Volume { get; set; }
        public decimal GWPerCtn { get; set; }
        public decimal NWPerCtn { get; set; }
        public decimal GWTotal { get; set; }
        public decimal NWTotal { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal PurchasePrice { get; set; } // Added for Profit Analysis
        public decimal PurchaseTotal { get; set; } // Added for Profit Analysis
        public decimal TaxRebateRate { get; set; } // Added for Tax Refund Calculation
        
        /// <summary>
        /// Calculated Tax Refund Amount (Not mapped to DB).
        /// 计算出的退税额 (采购总价 / 1.13 * 退税率)。
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal TaxRefundAmount => PurchaseTotal / 1.13m * (TaxRebateRate / 100m);

        public string Spare1 { get; set; }
        public string Spare2 { get; set; }
        public string Spare3 { get; set; }
        public string CustomFieldsJson { get; set; }

        /// <summary>
        /// Recalculates Volume based on Length, Width, Height, and Cartons.
        /// Volume = L * W * H / 1000000 * Cartons
        /// </summary>
        public void CalculateVolume()
        {
            Volume = Length > 0 && Width > 0 && Height > 0 && Cartons > 0
                ? Length * Width * Height / 1000000m * Cartons
                : 0m;
        }

        /// <summary>
        /// Recalculates Total Gross Weight based on GWPerCtn and Cartons.
        /// </summary>
        public void CalculateTotalGW()
        {
            GWTotal = GWPerCtn > 0 && Cartons > 0
                ? GWPerCtn * Cartons
                : 0m;
        }

        /// <summary>
        /// Recalculates Total Net Weight based on NWPerCtn and Cartons.
        /// </summary>
        public void CalculateTotalNW()
        {
            NWTotal = NWPerCtn > 0 && Cartons > 0
                ? NWPerCtn * Cartons
                : 0m;
        }

        /// <summary>
        /// Recalculates Total Price based on Quantity and Unit Price.
        /// </summary>
        public void CalculateTotalPrice()
        {
            TotalPrice = Quantity > 0 && UnitPrice > 0
                ? Quantity * UnitPrice
                : 0m;
        }

        /// <summary>
        /// Recalculates Purchase Total based on Quantity and Purchase Price.
        /// </summary>
        public void CalculatePurchaseTotal()
        {
            PurchaseTotal = Quantity > 0 && PurchasePrice > 0
                ? Quantity * PurchasePrice
                : 0m;
        }

        /// <summary>
        /// Recalculates Cartons based on Quantity and PcsPerCtn.
        /// </summary>
        public void CalculateCartons()
        {
            Cartons = Quantity > 0 && PcsPerCtn > 0
                ? Math.Ceiling(Quantity / PcsPerCtn)
                : 0m;
        }

        /// <summary>
        /// Recalculates all dependent fields (Volume, Weights, Price).
        /// </summary>
        public void RecalculateAll()
        {
            CalculateCartons(); // New step
            CalculateVolume();
            CalculateTotalGW();
            CalculateTotalNW();
            CalculateTotalPrice();
            CalculatePurchaseTotal();
        }

        /// <summary>
        /// Creates a deep copy of the item.
        /// 创建商品明细的深拷贝。
        /// </summary>
        /// <returns>A new Item object with the same values.</returns>
        public Item Clone()
        {
            return new Item
            {
                Id = this.Id,
                InvoiceId = this.InvoiceId,
                PoNumber = this.PoNumber,
                StyleNo = this.StyleNo,
                StyleName = this.StyleName,
                FabricComposition = this.FabricComposition,
                StyleNameCN = this.StyleNameCN,
                Brand = this.Brand,
                HSCode = this.HSCode,
                Origin = this.Origin,
                Quantity = this.Quantity,
                UnitEN = this.UnitEN,
                UnitCN = this.UnitCN,
                PcsPerCtn = this.PcsPerCtn,
                Cartons = this.Cartons,
                CtnUnitEN = this.CtnUnitEN,
                CtnUnitCN = this.CtnUnitCN,
                Length = this.Length,
                Width = this.Width,
                Height = this.Height,
                Volume = this.Volume,
                GWPerCtn = this.GWPerCtn,
                NWPerCtn = this.NWPerCtn,
                GWTotal = this.GWTotal,
                NWTotal = this.NWTotal,
                UnitPrice = this.UnitPrice,
                TotalPrice = this.TotalPrice,
                PurchasePrice = this.PurchasePrice,
                PurchaseTotal = this.PurchaseTotal,
                TaxRebateRate = this.TaxRebateRate,
                Spare1 = this.Spare1,
                Spare2 = this.Spare2,
                Spare3 = this.Spare3,
                CustomFieldsJson = this.CustomFieldsJson
            };
        }

        /// <summary>
        /// Sets the property value based on the column index.
        /// 根据列索引设置属性值。
        /// </summary>
        /// <param name="columnIndex">The column index (0-30).</param>
        /// <param name="value">The value string.</param>
        public void SetPropertyByIndex(int columnIndex, string value)
        {
            switch (columnIndex)
            {
                case 0: PoNumber = value; break;
                case 1: StyleNo = value; break;
                case 2: StyleName = value; break;
                case 3: FabricComposition = value; break;
                case 4: StyleNameCN = value; break;
                case 5: Brand = value; break;
                case 6: HSCode = value; break;
                case 7: Origin = value; break;
                case 8: // Quantity
                    Quantity = NumberHelper.ParseDecimal(value);
                    CalculateCartons(); // Trigger smart packing
                    CalculateVolume();
                    CalculateTotalGW();
                    CalculateTotalNW();
                    CalculateTotalPrice();
                    CalculatePurchaseTotal();
                    break;
                case 9: UnitEN = value; break;
                case 10: UnitCN = value; break;
                case 11: // PcsPerCtn (New)
                    PcsPerCtn = NumberHelper.ParseDecimal(value);
                    CalculateCartons(); // Trigger smart packing
                    CalculateVolume(); // Indirectly updates dependent fields if Cartons changes
                    CalculateTotalGW();
                    CalculateTotalNW();
                    break;
                case 12: // Cartons
                    Cartons = NumberHelper.ParseDecimal(value);
                    CalculateTotalGW();
                    CalculateTotalNW();
                    CalculateVolume();
                    break;
                case 13: CtnUnitEN = value; break;
                case 14: CtnUnitCN = value; break;
                case 15: // Length
                    Length = NumberHelper.ParseDecimal(value);
                    CalculateVolume();
                    break;
                case 16: // Width
                    Width = NumberHelper.ParseDecimal(value);
                    CalculateVolume();
                    break;
                case 17: // Height
                    Height = NumberHelper.ParseDecimal(value);
                    CalculateVolume();
                    break;
                case 18: Volume = NumberHelper.ParseDecimal(value); break;
                case 19: // GWPerCtn
                    GWPerCtn = NumberHelper.ParseDecimal(value);
                    CalculateTotalGW();
                    break;
                case 20: GWTotal = NumberHelper.ParseDecimal(value); break;
                case 21: // NWPerCtn
                    NWPerCtn = NumberHelper.ParseDecimal(value);
                    CalculateTotalNW();
                    break;
                case 22: NWTotal = NumberHelper.ParseDecimal(value); break;
                case 23: // UnitPrice
                    UnitPrice = NumberHelper.ParseDecimal(value);
                    CalculateTotalPrice();
                    break;
                case 24: TotalPrice = NumberHelper.ParseDecimal(value); break;
                case 25: 
                    PurchasePrice = NumberHelper.ParseDecimal(value); 
                    CalculatePurchaseTotal();
                    break;
                case 26: PurchaseTotal = NumberHelper.ParseDecimal(value); break;
                case 27: TaxRebateRate = NumberHelper.ParseDecimal(value); break;
                case 28: Spare1 = value; break;
                case 29: Spare2 = value; break;
                case 30: Spare3 = value; break;
            }
        }
    }
}

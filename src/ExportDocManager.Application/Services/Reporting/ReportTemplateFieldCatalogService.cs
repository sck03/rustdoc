namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportTemplateFieldCatalogService : IReportTemplateFieldCatalogService
    {
        private static readonly string[] CategoryOrder =
        [
            "单据信息",
            "客户信息",
            "出口商信息",
            "商品明细",
            "付款报销",
            "金额换算",
            "其它字段"
        ];

        private static readonly IReadOnlyList<ReportTemplateFieldDescriptor> ExportDocumentFields =
        [
            Export("单据信息", "发票号码 (Invoice No)", "{{ Invoice.InvoiceNo }}"),
            Export("单据信息", "合同号码 (Contract No)", "{{ Invoice.ContractNo }}"),
            Export("单据信息", "发票日期 (Date)", "{{ Invoice.InvoiceDate | date.to_string '%Y-%m-%d' }}"),
            Export("单据信息", "信用证号 (L/C No)", "{{ Invoice.LetterOfCreditNo }}"),
            Export("单据信息", "开证行 (Issuing Bank)", "{{ Invoice.IssuingBank }}"),
            Export("单据信息", "付款方式 (Payment Terms)", "{{ Invoice.PaymentTerms }}"),
            Export("单据信息", "贸易条款 (Trade Terms)", "{{ Invoice.TradeTerms }}"),
            Export("单据信息", "特殊条款 (Special Terms)", "{{ Invoice.SpecialTerms }}"),
            Export("单据信息", "监管方式 (Supervision Mode)", "{{ Invoice.SupervisionMode }}"),
            Export("单据信息", "起运港 (Port of Loading)", "{{ Invoice.PortOfLoading }}"),
            Export("单据信息", "目的港 (Port of Destination)", "{{ Invoice.PortOfDestination }}"),
            Export("单据信息", "目的国 (Destination Country)", "{{ Invoice.DestinationCountry }}"),
            Export("单据信息", "运输方式 (Transport Mode)", "{{ Invoice.TransportMode }}"),
            Export("单据信息", "船期/航期 (Shipment Date)", "{{ Invoice.ShipmentDate | date.to_string '%Y-%m-%d' }}"),
            Export("单据信息", "唛头 (Shipping Marks)", "{{ Invoice.ShippingMarks }}"),
            Export("客户信息", "客户英文名 (Customer Name EN)", "{{ Customer.CustomerNameEN }}"),
            Export("客户信息", "客户英文地址 (Customer Address EN)", "{{ Customer.AddressEN }}"),
            Export("客户信息", "通知人名称 (Notify Party Name)", "{{ Customer.NotifyPartyName }}"),
            Export("客户信息", "通知人地址 (Notify Party Address)", "{{ Customer.NotifyPartyAddress }}"),
            Export("出口商信息", "我方英文名 (Exporter Name EN)", "{{ Exporter.ExporterNameEN }}"),
            Export("出口商信息", "我方英文地址 (Exporter Address EN)", "{{ Exporter.AddressEN }}"),
            Export("出口商信息", "我方中文名 (Exporter Name CN)", "{{ Exporter.ExporterNameCN }}"),
            Export("出口商信息", "我方中文地址 (Exporter Address CN)", "{{ Exporter.AddressCN }}"),
            Export("出口商信息", "统一社会信用代码 (Credit Code)", "{{ Exporter.CreditCode }}"),
            Export("出口商信息", "开户行名称 (Bank Name)", "{{ Exporter.BankName }}"),
            Export("出口商信息", "银行账号 (Bank Account)", "{{ Exporter.BankAccount }}"),
            Export("出口商信息", "Swift Code", "{{ Exporter.SwiftCode }}"),
            Export("单据信息", "币种 (Currency)", "{{ Invoice.Currency }}"),
            Export("单据信息", "总金额 (Total Amount)", "{{ Invoice.TotalAmount }}"),
            Export("单据信息", "总数量 (Total Quantity)", "{{ Invoice.TotalQuantity }}"),
            Export("单据信息", "总箱数 (Total Cartons)", "{{ Invoice.TotalCartons }}"),
            Export("单据信息", "总毛重 (Total Gross Weight)", "{{ Invoice.TotalGrossWeight }}"),
            Export("单据信息", "总净重 (Total Net Weight)", "{{ Invoice.TotalNetWeight }}"),
            Export("单据信息", "总体积 (Total Volume)", "{{ Invoice.TotalVolume }}"),
            Export("单据信息", "【内部】采购总额 (Purchase Amount)", "{{ Invoice.TotalPurchaseAmount }}"),
            Export("单据信息", "【内部】退税总额 (Tax Refund)", "{{ Invoice.TotalTaxRefundAmount }}"),
            Export("单据信息", "【内部】利润总额 (Total Profit)", "{{ Invoice.TotalProfit }}"),
            Export("单据信息", "单据备用字段1 (Invoice Spare1)", "{{ Invoice.Spare1 }}"),
            Export("单据信息", "单据备用字段2 (Invoice Spare2)", "{{ Invoice.Spare2 }}"),
            Export("单据信息", "单据备用字段3 (Invoice Spare3)", "{{ Invoice.Spare3 }}"),
            Export("商品明细", "商品款号 (Item Style No)", "{{ item.StyleNo }}"),
            Export("商品明细", "商品名称 (Item Name)", "{{ item.StyleName }}"),
            Export("商品明细", "商品描述 (Item Description)", "{{ item.Description }}"),
            Export("商品明细", "客户PO (Item PO)", "{{ item.PoNumber }}"),
            Export("商品明细", "HS编码 (Item HS Code)", "{{ item.HsCode }}"),
            Export("商品明细", "商品数量 (Item Qty)", "{{ item.Quantity }}"),
            Export("商品明细", "数量单位 (Item Qty Unit)", "{{ item.UnitEN }}"),
            Export("商品明细", "单价 (Item Unit Price)", "{{ item.UnitPrice }}"),
            Export("商品明细", "总价 (Item Total Price)", "{{ item.TotalPrice }}"),
            Export("商品明细", "箱数 (Item Cartons)", "{{ item.Cartons }}"),
            Export("商品明细", "箱数单位 (Item Ctn Unit)", "{{ item.CtnUnitEN }}"),
            Export("商品明细", "单箱毛重 (Item GW Per Ctn)", "{{ item.GWPerCtn }}"),
            Export("商品明细", "单箱净重 (Item NW Per Ctn)", "{{ item.NWPerCtn }}"),
            Export("商品明细", "总毛重 (Item Total GW)", "{{ item.GWTotal }}"),
            Export("商品明细", "总净重 (Item Total NW)", "{{ item.NWTotal }}"),
            Export("商品明细", "商品体积 (Item Volume)", "{{ item.Volume }}"),
            Export("商品明细", "明细备用字段1 (Item Spare1)", "{{ item.Spare1 }}"),
            Export("商品明细", "明细备用字段2 (Item Spare2)", "{{ item.Spare2 }}"),
            Export("商品明细", "明细备用字段3 (Item Spare3)", "{{ item.Spare3 }}")
        ];

        private static readonly IReadOnlyList<ReportTemplateFieldDescriptor> PaymentVoucherFields =
        [
            Payment("付款报销", "付款/报销单号 (Payment ID)", "{{ Payment.Id }}"),
            Payment("付款报销", "发票号/业务参考号 (Text)", "{{ Payment.InvoiceNo }}"),
            Payment("付款报销", "申请日期 (Payment Date)", "{{ Payment.PaymentDate | date.to_string '%Y-%m-%d' }}"),
            Payment("付款报销", "部门 (Department)", "{{ Payment.Department }}"),
            Payment("付款报销", "项目/业务号 (Project)", "{{ Payment.Project }}"),
            Payment("付款报销", "付款方式 (Payment Method)", "{{ Payment.PaymentMethod }}"),
            Payment("付款报销", "收款人名称 (Payee Name)", "{{ Payment.PayeeName }}"),
            Payment("付款报销", "收款人开户行 (Bank Name)", "{{ Payment.BankName }}"),
            Payment("付款报销", "收款人账号 (Account No)", "{{ Payment.AccountNo }}"),
            Payment("付款报销", "申请人/付款人 (Payer Name)", "{{ Payment.PayerName }}"),
            Payment("付款报销", "外币金额 (USD Amount)", "{{ Payment.USDAmount }}"),
            Payment("付款报销", "人民币金额 (CNY Amount)", "{{ Payment.CNYAmount }}"),
            Payment("金额换算", "人民币大写 (CNY Upper)", "{{ cny_amount_upper }}"),
            Payment("付款报销", "差旅费 (Travel Expense)", "{{ Payment.TravelExpense }}"),
            Payment("付款报销", "业务招待费 (Entertainment Expense)", "{{ Payment.BusinessEntertainmentExpense }}"),
            Payment("付款报销", "通讯费 (Telephone Expense)", "{{ Payment.TelephoneExpense }}"),
            Payment("付款报销", "办公费 (Office Expense)", "{{ Payment.OfficeExpense }}"),
            Payment("付款报销", "维修费 (Repair Expense)", "{{ Payment.RepairExpense }}"),
            Payment("付款报销", "运杂费 (Freight Misc Expense)", "{{ Payment.FreightMiscExpense }}"),
            Payment("付款报销", "商检/检验费 (Inspection Expense)", "{{ Payment.InspectionExpense }}"),
            Payment("付款报销", "其他费用 (Other Expense)", "{{ Payment.OtherExpense }}"),
            Payment("付款报销", "货品名称 (Goods Name)", "{{ Payment.GoodsName }}"),
            Payment("付款报销", "数量 (Quantity)", "{{ Payment.Quantity }}"),
            Payment("付款报销", "出运国 (Shipment Country)", "{{ Payment.ShipmentCountry }}"),
            Payment("付款报销", "出运日期 (Shipment Date)", "{{ Payment.ShipmentDate | date.to_string '%Y-%m-%d' }}"),
            Payment("付款报销", "收单日期 (Receipt Date)", "{{ Payment.ReceiptDate | date.to_string '%Y-%m-%d' }}"),
            Payment("付款报销", "备注 (Notes)", "{{ Payment.Notes }}")
        ];

        public ReportTemplateFieldCatalog GetFieldCatalog(ReportDocumentType reportType)
        {
            return new ReportTemplateFieldCatalog
            {
                ReportType = reportType,
                CategoryOrder = CategoryOrder,
                Fields = reportType == ReportDocumentType.PaymentVoucher
                    ? PaymentVoucherFields
                    : ExportDocumentFields
            };
        }

        private static ReportTemplateFieldDescriptor Export(string category, string label, string value)
        {
            return Field(ReportDocumentType.ExportDocument, category, label, value);
        }

        private static ReportTemplateFieldDescriptor Payment(string category, string label, string value)
        {
            return Field(ReportDocumentType.PaymentVoucher, category, label, value);
        }

        private static ReportTemplateFieldDescriptor Field(
            ReportDocumentType reportType,
            string category,
            string label,
            string value)
        {
            return new ReportTemplateFieldDescriptor
            {
                ReportType = reportType,
                Category = category,
                Label = label,
                Value = value
            };
        }
    }
}

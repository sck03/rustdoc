using System.ComponentModel;

namespace ExportDocManager.Models.DTOs
{
    public class QueryResultRow
    {
        public int Id { get; set; }

        [DisplayName("发票号")]
        public string InvoiceNo { get; set; }

        [DisplayName("日期")]
        public string InvoiceDate { get; set; }

        [DisplayName("合同号")]
        public string ContractNo { get; set; }

        [DisplayName("客户")]
        public string CustomerName { get; set; }

        [DisplayName("出口商")]
        public string ExporterName { get; set; }

        [DisplayName("目的国")]
        public string DestinationCountry { get; set; }

        [DisplayName("贸易条款")]
        public string TradeTerms { get; set; }

        [DisplayName("船期/航期")]
        public string ShipmentDate { get; set; }

        [DisplayName("运输方式")]
        public string TransportMode { get; set; }

        [DisplayName("总箱数")]
        public decimal TotalCartons { get; set; }

        [DisplayName("总数量")]
        public decimal TotalQuantity { get; set; }

        [DisplayName("总金额")]
        public decimal TotalAmount { get; set; }

        [DisplayName("币种")]
        public string Currency { get; set; }

        [DisplayName("类型")]
        public string Type { get; set; }
    }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class Payment
    {
        public int Id { get; set; }
        public int? OwnerUserId { get; set; }
        public string DepartmentId { get; set; } = string.Empty;
        public string CompanyScope { get; set; } = string.Empty;
        public string InvoiceNo { get; set; }
        public DateTime ShipmentDate { get; set; }
        public int PayeeId { get; set; }
        public string Department { get; set; }
        public string Project { get; set; }
        public decimal USDAmount { get; set; }
        public decimal CNYAmount { get; set; }
        public string PaymentMethod { get; set; }
        public string PayeeName { get; set; }
        public string PayerName { get; set; }
        public string BankName { get; set; }
        public string AccountNo { get; set; }
        public string Notes { get; set; }
        public DateTime PaymentDate { get; set; }

        public string GoodsName { get; set; }
        public string Quantity { get; set; }
        public string ShipmentCountry { get; set; }
        public DateTime ReceiptDate { get; set; }

        public decimal TravelExpense { get; set; }
        public decimal BusinessEntertainmentExpense { get; set; }
        public decimal TelephoneExpense { get; set; }
        public decimal OfficeExpense { get; set; }
        public decimal RepairExpense { get; set; }
        public decimal FreightMiscExpense { get; set; }
        public decimal InspectionExpense { get; set; }
        public decimal OtherExpense { get; set; }

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; }
    }
}

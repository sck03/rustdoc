namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiPaymentDto
    {
        public int Id { get; init; }
        public int? OwnerUserId { get; init; }
        public string DepartmentId { get; init; } = string.Empty;
        public string CompanyScope { get; init; } = string.Empty;
        public string InvoiceNo { get; init; } = string.Empty;
        public DateTime ShipmentDate { get; init; }
        public int PayeeId { get; init; }
        public string Department { get; init; } = string.Empty;
        public string Project { get; init; } = string.Empty;
        public decimal USDAmount { get; init; }
        public decimal CNYAmount { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public string PayeeName { get; init; } = string.Empty;
        public string PayerName { get; init; } = string.Empty;
        public string BankName { get; init; } = string.Empty;
        public string AccountNo { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
        public DateTime PaymentDate { get; init; }
        public string GoodsName { get; init; } = string.Empty;
        public string Quantity { get; init; } = string.Empty;
        public string ShipmentCountry { get; init; } = string.Empty;
        public DateTime ReceiptDate { get; init; }
        public decimal TravelExpense { get; init; }
        public decimal BusinessEntertainmentExpense { get; init; }
        public decimal TelephoneExpense { get; init; }
        public decimal OfficeExpense { get; init; }
        public decimal RepairExpense { get; init; }
        public decimal FreightMiscExpense { get; init; }
        public decimal InspectionExpense { get; init; }
        public decimal OtherExpense { get; init; }

        public string RowVersion { get; init; } = string.Empty;
    }

    public sealed record ApiPaymentSaveResponse(
        bool Success,
        int Id,
        ApiPaymentDto Payment);
}

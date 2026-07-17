using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public sealed class SupplierProductLink
    {
        public int Id { get; set; }
        public int SupplierCompanyId { get; set; }
        public int ProductId { get; set; }
        [MaxLength(100)] public string SupplierProductCode { get; set; } = string.Empty;
        public decimal ReferencePrice { get; set; }
        [MaxLength(3)] public string Currency { get; set; } = "CNY";
        public int LeadTimeDays { get; set; }
        [MaxLength(30)] public string Status { get; set; } = "供货中";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

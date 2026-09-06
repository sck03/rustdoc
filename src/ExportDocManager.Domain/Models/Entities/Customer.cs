using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Represents a customer entity.
    /// 代表一个客户实体。
    /// </summary>
    public class Customer : IBusinessOwnedEntity
    {
        public int Id { get; set; }
        public int? OwnerUserId { get; set; }
        public string DepartmentId { get; set; } = string.Empty;
        public string CompanyScope { get; set; } = string.Empty;
        public string? CustomerNameEN { get; set; }
        public string DisplayName => CustomerNameEN ?? string.Empty;
        public NotifyPartyMode NotifyPartyMode { get; set; }
        public string? NotifyPartyName { get; set; } // 通知人名称，原 CustomerNameCN
        public string? AddressEN { get; set; }
        public string? NotifyPartyAddress { get; set; } // 通知人地址，原 AddressCN
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? TaxId { get; set; }
        public string? Notes { get; set; }
        // 移除 IsConsignee 和 IsNotifyParty 字段

        [ConcurrencyCheck]
        public byte[]? RowVersion { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Represents a customer entity.
    /// 代表一个客户实体。
    /// </summary>
    public class Customer
    {
        public int Id { get; set; }
        public string CustomerNameEN { get; set; }
        public string DisplayName =>
            string.IsNullOrWhiteSpace(NotifyPartyName)
                ? CustomerNameEN ?? string.Empty
                : $"{CustomerNameEN} ({NotifyPartyName})";
        public string NotifyPartyName { get; set; } // 通知人名称，原 CustomerNameCN
        public string AddressEN { get; set; }
        public string NotifyPartyAddress { get; set; } // 通知人地址，原 AddressCN
        public string ContactPerson { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string TaxId { get; set; }
        public string Notes { get; set; }
        // 移除 IsConsignee 和 IsNotifyParty 字段

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; }
    }
}

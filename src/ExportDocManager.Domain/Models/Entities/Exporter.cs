using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Represents an exporter entity.
    /// 代表一个出口商实体。
    /// </summary>
    public class Exporter
    {
        public int Id { get; set; }
        public string ExporterNameEN { get; set; }
        public string ExporterNameCN { get; set; }
        public string AddressEN { get; set; }
        public string AddressCN { get; set; }
        public string ContactPerson { get; set; }
        public string CreditCode { get; set; }
        public string CustomsCode { get; set; }
        public string Phone { get; set; }
        public string BankName { get; set; }
        public string BankAccount { get; set; }
        public string SwiftCode { get; set; }
        public string Notes { get; set; }
        public string DocSealPath { get; set; }
        public string CustomsSealPath { get; set; }

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; }
    }
}

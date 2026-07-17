using System;
using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class CustomsCooProducerProfile
    {
        public int Id { get; set; }

        [MaxLength(20)]
        public string CiqRegNo { get; set; } = string.Empty;

        [MaxLength(400)]
        public string PrdcEtpsName { get; set; } = string.Empty;

        [MaxLength(80)]
        public string PrdcEtpsConcEr { get; set; } = string.Empty;

        [MaxLength(80)]
        public string PrdcEtpsTel { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Producer { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ProducerTel { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ProducerFax { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ProducerEmail { get; set; } = string.Empty;

        [MaxLength(10)]
        public string ProducerSertFlag { get; set; } = string.Empty;

        [MaxLength(80)]
        public string LastInvoiceNo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string LastContractNo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string LastSourceStyleNo { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public DateTime LastUsedAt { get; set; } = DateTime.Now;
    }
}

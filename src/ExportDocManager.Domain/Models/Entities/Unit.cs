using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class Unit
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string NameEN { get; set; } // English Unit (e.g., PCS, SETS)

        [MaxLength(20)]
        public string NameCN { get; set; } // Chinese Unit (e.g., 件, 套)

        [MaxLength(10)]
        public string Code { get; set; } // Optional Code

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; }
    }
}

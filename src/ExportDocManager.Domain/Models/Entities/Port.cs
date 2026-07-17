using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class Port
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string NameEN { get; set; } // English Name (Primary)

        [MaxLength(100)]
        public string NameCN { get; set; } // Chinese Name

        [MaxLength(50)]
        public string Country { get; set; }

        [MaxLength(20)]
        public string Code { get; set; } // Port Code (e.g., UN/LOCODE)
    }
}

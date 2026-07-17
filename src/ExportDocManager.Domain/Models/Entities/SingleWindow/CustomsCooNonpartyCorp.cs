using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class CustomsCooNonpartyCorp
    {
        public int Id { get; set; }

        public int DocumentId { get; set; }

        public int SortNo { get; set; }

        [MaxLength(500)]
        public string EntName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string EntAddr { get; set; } = string.Empty;

        [MaxLength(10)]
        public string EntCountryCode { get; set; } = string.Empty;

        [MaxLength(100)]
        public string EntCountryName { get; set; } = string.Empty;

        public CustomsCooDocument Document { get; set; }
    }
}

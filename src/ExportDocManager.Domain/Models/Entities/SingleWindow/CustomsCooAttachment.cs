using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class CustomsCooAttachment
    {
        public int Id { get; set; }

        public int DocumentId { get; set; }

        [MaxLength(80)]
        public string CertNo { get; set; } = string.Empty;

        [MaxLength(20)]
        public string CertType { get; set; } = string.Empty;

        [MaxLength(40)]
        public string AplRegNo { get; set; } = string.Empty;

        [MaxLength(40)]
        public string CiqRegNo { get; set; } = string.Empty;

        [MaxLength(20)]
        public string FileType { get; set; } = string.Empty;

        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(520)]
        public string FilePath { get; set; } = string.Empty;

        [MaxLength(120)]
        public string MediaType { get; set; } = string.Empty;

        [MaxLength(260)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(20)]
        public string DocType { get; set; } = string.Empty;

        public bool IsDelay { get; set; }

        public bool FileExistsAtBuild { get; set; }

        public int SortOrder { get; set; }

        public CustomsCooDocument Document { get; set; }
    }
}

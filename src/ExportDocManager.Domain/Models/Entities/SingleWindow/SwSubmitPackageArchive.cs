using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwSubmitPackageArchive
    {
        public int Id { get; set; }

        public int BatchId { get; set; }

        public long SizeBytes { get; set; }

        [MaxLength(64)]
        public string Sha256 { get; set; } = string.Empty;

        public byte[] Content { get; set; } = [];

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public SwSubmissionBatch? Batch { get; set; }
    }
}

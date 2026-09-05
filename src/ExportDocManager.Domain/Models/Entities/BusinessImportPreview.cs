using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Short-lived, server-owned import snapshot. The browser receives only
    /// the opaque identifier; validated rows never round-trip as commit input.
    /// </summary>
    public sealed class BusinessImportPreview
    {
        [Key, MaxLength(32)]
        public string Id { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Kind { get; set; } = string.Empty;

        public int OwnerUserId { get; set; }

        public int RowCount { get; set; }

        [Required, MaxLength(64)]
        public string PayloadSha256 { get; set; } = string.Empty;

        [Required]
        public string PayloadJson { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? ConsumedAt { get; set; }

        [ConcurrencyCheck]
        public int VersionNumber { get; set; } = 1;
    }
}

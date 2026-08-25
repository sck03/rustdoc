using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities;

public sealed class EmailDeliveryRecord
{
    [Key, MaxLength(120)]
    public string DeliveryId { get; set; } = string.Empty;

    [MaxLength(120)] public string JobId { get; set; } = string.Empty;
    [MaxLength(80)] public string Kind { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string RequestFingerprint { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    [MaxLength(100)] public string RequestedBy { get; set; } = string.Empty;
    [MaxLength(320)] public string Recipient { get; set; } = string.Empty;
    [MaxLength(300)] public string Subject { get; set; } = string.Empty;
    public int AttachmentCount { get; set; }
    [Required, MaxLength(24)] public string Status { get; set; } = EmailDeliveryStatus.Attempting;
    [MaxLength(4000)] public string ErrorMessage { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class EmailDeliveryStatus
{
    public const string Attempting = "Attempting";
    public const string Sent = "Sent";
    public const string Uncertain = "Uncertain";
}

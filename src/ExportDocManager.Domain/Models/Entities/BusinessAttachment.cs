using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities;

public enum BusinessAttachmentCategory { Original, Confirmation, FinalOutput }

/// <summary>Documents inherit access and retention from their source invoice.</summary>
public sealed class BusinessAttachment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    public BusinessAttachmentCategory Category { get; set; }
    [MaxLength(100)] public string PoNumber { get; set; } = string.Empty;
    [MaxLength(200)] public string StyleNo { get; set; } = string.Empty;
    [MaxLength(510)] public string SearchKey { get; set; } = string.Empty;
    public int LatestRevision { get; set; }
    public int? CurrentRevision { get; set; }
    public bool IsArchived { get; set; }
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<BusinessAttachmentRevision> Revisions { get; set; } = [];
}

/// <summary>Immutable bytes and metadata are committed and backed up together.</summary>
public sealed class BusinessAttachmentRevision
{
    public int Id { get; set; }
    public int BusinessAttachmentId { get; set; }
    public int Revision { get; set; }
    public Guid UploadKey { get; set; }
    [Required, MaxLength(240)] public string FileName { get; set; } = string.Empty;
    [MaxLength(240)] public string FileNameNormalized { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ContentType { get; set; } = string.Empty;
    public int Length { get; set; }
    [Required, MaxLength(64)] public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public int UploadedByUserId { get; set; }
    [Required, MaxLength(100)] public string UploadedBy { get; set; } = string.Empty;
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BusinessAttachmentEvent
{
    public int Id { get; set; }
    public int BusinessAttachmentId { get; set; }
    [Required, MaxLength(30)] public string Action { get; set; } = string.Empty;
    public int? Revision { get; set; }
    public int ActorUserId { get; set; }
    [Required, MaxLength(100)] public string ActorName { get; set; } = string.Empty;
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

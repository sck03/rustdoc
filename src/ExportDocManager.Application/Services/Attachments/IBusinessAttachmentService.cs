using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Attachments;

public interface IBusinessAttachmentService
{
    Task<BusinessAttachmentPage> QueryAsync(BusinessAttachmentQuery query, CancellationToken cancellationToken = default);
    Task<BusinessAttachmentDetails> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<BusinessAttachmentRecord> UploadAsync(int invoiceId, BusinessAttachmentUpload request, Stream content, CancellationToken cancellationToken = default);
    Task<BusinessAttachmentRecord> UpdateAsync(int id, BusinessAttachmentUpdate request, CancellationToken cancellationToken = default);
    Task<BusinessAttachmentContent> ReadAsync(int id, int revision, CancellationToken cancellationToken = default);
}

public sealed record BusinessAttachmentQuery(int? InvoiceId = null, string? Keyword = null, bool IncludeArchived = false, int PageNumber = 1, int PageSize = 20);
public sealed record BusinessAttachmentPage(PagedResult<BusinessAttachmentRecord> Page, bool CanUpload,
    long? UsedBytes, int FileBytesLimit, long InvoiceBytesLimit);
public sealed record BusinessAttachmentUpload(int? AttachmentId, int ExpectedVersion, Guid UploadKey, string Title,
    BusinessAttachmentCategory Category, string PoNumber, string StyleNo, string FileName, string Note);
public sealed record BusinessAttachmentUpdate(int ExpectedVersion, int? CurrentRevision, bool IsArchived, string Note);
public sealed record BusinessAttachmentRecord(int Id, int InvoiceId, string InvoiceNo, string InvoiceType, string CustomerName,
    string Title, BusinessAttachmentCategory Category, string PoNumber, string StyleNo, int LatestRevision,
    int? CurrentRevision, bool IsArchived, int VersionNumber, DateTimeOffset UpdatedAt, bool CanEdit);
public sealed record BusinessAttachmentRevisionRecord(int Revision, string FileName, string ContentType, int Length,
    string Sha256, string UploadedBy, string Note, DateTimeOffset CreatedAt);
public sealed record BusinessAttachmentEventRecord(string Action, int? Revision, string ActorName, string Note, DateTimeOffset CreatedAt);
public sealed record BusinessAttachmentDetails(BusinessAttachmentRecord Attachment,
    IReadOnlyList<BusinessAttachmentRevisionRecord> Revisions, IReadOnlyList<BusinessAttachmentEventRecord> Events, int EventCount);
public sealed record BusinessAttachmentContent(string FileName, string ContentType, byte[] Content);

public static class BusinessAttachmentLimits
{
    public const int FileBytes = 16 * 1024 * 1024;
    public const long InvoiceBytes = 256L * 1024 * 1024;
    public const int AttachmentsPerInvoice = 100;
    public const int RevisionsPerAttachment = 50;
}

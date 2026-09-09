using System.Security.Cryptography;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Attachments;

public sealed partial class BusinessAttachmentService
{
    public Task<BusinessAttachmentPage> QueryAsync(BusinessAttachmentQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(false, async (db, actor, token) =>
        {
            if (query.PageNumber is < 1 or > 1000000 || query.PageSize is < 1 or > 100 || query.InvoiceId is <= 0)
                throw new ServiceValidationException("查询页码、每页数量或发票编号无效。");
            var invoice = query.InvoiceId is int invoiceId ? await InvoiceAsync(db, invoiceId, actor, PermissionAction.View, false, token) : null;
            string keyword = BusinessAttachmentFilePolicy.Text(query.Keyword, "搜索词", 100).ToUpperInvariant();
            var attachments = Accessible(db, actor).Where(item =>
                (!query.InvoiceId.HasValue || item.InvoiceId == query.InvoiceId) && (query.IncludeArchived || !item.IsArchived) &&
                (keyword == "" || item.SearchKey.Contains(keyword) || item.Invoice.SearchKey.Contains(keyword) ||
                 item.Revisions.Any(revision => revision.FileNameNormalized.Contains(keyword))));
            int count = await attachments.CountAsync(token);
            var rows = await Records(db, attachments.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
                .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize), actor).ToListAsync(token);
            long? used = invoice == null ? null : await (from version in db.BusinessAttachmentRevisions
                                                         join item in db.BusinessAttachments on version.BusinessAttachmentId equals item.Id
                                                         where item.InvoiceId == invoice.Id
                                                         select (long?)version.Length).SumAsync(token) ?? 0;
            bool canUpload = invoice != null && runtime.IsPermissionAvailable(Resource, PermissionAction.Operate) &&
                access.CanAccessOwnedBusinessRecord(invoice.OwnerUserId, invoice.DepartmentId, invoice.CompanyScope, Resource, PermissionAction.Operate, actor);
            return new BusinessAttachmentPage(new PagedResult<BusinessAttachmentRecord>(rows, count, query.PageNumber, query.PageSize),
                canUpload, used, BusinessAttachmentLimits.FileBytes, BusinessAttachmentLimits.InvoiceBytes);
        }, cancellationToken);

    public Task<BusinessAttachmentDetails> GetAsync(int id, CancellationToken cancellationToken = default) =>
        RunAsync(false, async (db, actor, token) =>
        {
            await AttachmentAsync(db, id, actor, PermissionAction.View, token);
            var revisions = await db.BusinessAttachmentRevisions.AsNoTracking().Where(item => item.BusinessAttachmentId == id)
                .OrderByDescending(item => item.Revision).Select(item => new BusinessAttachmentRevisionRecord(item.Revision,
                    item.FileName, item.ContentType, item.Length, item.Sha256, item.UploadedBy, item.Note, item.CreatedAt)).ToListAsync(token);
            var history = db.BusinessAttachmentEvents.AsNoTracking().Where(item => item.BusinessAttachmentId == id);
            var events = await history.OrderByDescending(item => item.Id).Take(100)
                .Select(item => new BusinessAttachmentEventRecord(item.Action, item.Revision, item.ActorName, item.Note, item.CreatedAt)).ToListAsync(token);
            return new BusinessAttachmentDetails(await RecordAsync(db, id, actor, token), revisions, events, await history.CountAsync(token));
        }, cancellationToken);

    public Task<BusinessAttachmentContent> ReadAsync(int id, int revision, CancellationToken cancellationToken = default) =>
        RunAsync(false, async (db, actor, token) =>
        {
            await AttachmentAsync(db, id, actor, PermissionAction.View, token);
            var file = await db.BusinessAttachmentRevisions.AsNoTracking()
                .SingleOrDefaultAsync(item => item.BusinessAttachmentId == id && item.Revision == revision, token)
                ?? throw new ResourceNotFoundException("资料版本不存在。");
            if (file.Length != file.Content.Length || file.Sha256 != Convert.ToHexString(SHA256.HashData(file.Content)))
                throw new UserVisibleInfrastructureException("归档文件完整性校验失败，请联系管理员从备份恢复。");
            return new BusinessAttachmentContent(file.FileName, file.ContentType, file.Content);
        }, cancellationToken);

    private IQueryable<BusinessAttachment> Accessible(AppDbContext db, User actor)
    {
        var invoices = access.ApplyInvoiceScope(db.Invoices.AsNoTracking(), actor);
        return db.BusinessAttachments.AsNoTracking().Where(item => invoices.Any(invoice => invoice.Id == item.InvoiceId));
    }

    private IQueryable<BusinessAttachmentRecord> Records(AppDbContext db, IQueryable<BusinessAttachment> query, User actor)
    {
        bool mayEdit = runtime.IsPermissionAvailable(Resource, PermissionAction.Operate) && access.HasPermission(Resource, PermissionAction.Operate, actor);
        var editable = access.ApplyInvoiceScopeForPermission(db.Invoices, Resource, PermissionAction.Operate, actor);
        bool mayDelete = runtime.IsPermissionAvailable(Resource, PermissionAction.Manage) && access.HasPermission(Resource, PermissionAction.Manage, actor);
        var deletable = access.ApplyInvoiceScopeForPermission(db.Invoices, Resource, PermissionAction.Manage, actor);
        return query.Select(item => new BusinessAttachmentRecord(item.Id, item.InvoiceId, item.Invoice.InvoiceNo, item.Invoice.Type ?? "",
            item.Invoice.CustomerNameEN ?? "", item.Title, item.CategoryId, item.Category.Name, item.PoNumber, item.StyleNo, item.LatestRevision,
            item.CurrentRevision, item.IsArchived, item.VersionNumber, item.UpdatedAt,
            mayEdit && editable.Any(invoice => invoice.Id == item.InvoiceId),
            mayDelete && deletable.Any(invoice => invoice.Id == item.InvoiceId)));
    }

    private async Task<BusinessAttachmentRecord> RecordAsync(AppDbContext db, int id, User actor, CancellationToken token) =>
        await Records(db, Accessible(db, actor).Where(item => item.Id == id), actor).SingleOrDefaultAsync(token)
        ?? throw new ResourceNotFoundException("业务资料不存在。");
}

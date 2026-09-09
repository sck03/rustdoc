using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Attachments;

public sealed partial class BusinessAttachmentService
{
    public Task<BusinessAttachmentRecord> UpdateAsync(int id, BusinessAttachmentUpdate request, CancellationToken cancellationToken = default) =>
        RunAsync(true, async (db, actor, token) =>
        {
            Demand(PermissionAction.Operate, actor);
            var item = await AttachmentAsync(db, id, actor, PermissionAction.Operate, token);
            Version(request.ExpectedVersion, item.VersionNumber);
            string note = BusinessAttachmentFilePolicy.Text(request.Note, "操作说明", 500, true);
            if (request.CurrentRevision is <= 0 || request.CurrentRevision > item.LatestRevision ||
                request.CurrentRevision == null && item.CurrentRevision != null)
                throw new ServiceValidationException("请选择该资料已有的版本。");
            if (item.CurrentRevision == request.CurrentRevision && item.IsArchived == request.IsArchived)
                return await RecordAsync(db, id, actor, token);
            string action = item.IsArchived != request.IsArchived ? request.IsArchived ? "Archive" : "Restore" : "Confirm";
            if (item.IsArchived && request.CurrentRevision != item.CurrentRevision)
                throw new ResourceConflictException("请先恢复资料，再确认有效版本。");
            if (request.CurrentRevision != item.CurrentRevision && request.IsArchived != item.IsArchived)
                throw new ServiceValidationException("确认版本与停用或恢复须分别操作。");
            item.CurrentRevision = request.CurrentRevision;
            item.IsArchived = request.IsArchived;
            AddEvent(db, item, actor, action, note);
            await db.SaveChangesAsync(token);
            return await RecordAsync(db, id, actor, token);
        }, cancellationToken);

    public Task<BusinessAttachmentRecord> EditMetadataAsync(int id, BusinessAttachmentMetadataUpdate request, CancellationToken cancellationToken = default) =>
        RunAsync(true, async (db, actor, token) =>
        {
            Demand(PermissionAction.Operate, actor);
            var item = await AttachmentAsync(db, id, actor, PermissionAction.Operate, token);
            Version(request.ExpectedVersion, item.VersionNumber);
            var company = await db.Invoices.Where(invoice => invoice.Id == item.InvoiceId).Select(invoice => invoice.CompanyScope).SingleAsync(token);
            await DemandCategoryAsync(db, request.CategoryId, company, token);
            string title = BusinessAttachmentFilePolicy.Text(request.Title, "资料名称", 200, true);
            string po = BusinessAttachmentFilePolicy.Text(request.PoNumber, "PO 号", 100);
            string style = BusinessAttachmentFilePolicy.Text(request.StyleNo, "款号", 200);
            string note = BusinessAttachmentFilePolicy.Text(request.Note, "修正说明", 500, true);
            if (item.Title == title && item.CategoryId == request.CategoryId && item.PoNumber == po && item.StyleNo == style)
                return await RecordAsync(db, id, actor, token);
            item.Title = title;
            item.CategoryId = request.CategoryId;
            item.PoNumber = po;
            item.StyleNo = style;
            AddEvent(db, item, actor, "Edit", note);
            await db.SaveChangesAsync(token);
            return await RecordAsync(db, id, actor, token);
        }, cancellationToken);

    public Task DeleteAsync(int id, BusinessAttachmentDelete request, CancellationToken cancellationToken = default) =>
        RunAsync(true, async (db, actor, token) =>
        {
            Demand(PermissionAction.Manage, actor);
            var item = await AttachmentAsync(db, id, actor, PermissionAction.Manage, token);
            Version(request.ExpectedVersion, item.VersionNumber);
            string note = BusinessAttachmentFilePolicy.Text(request.Note, "删除原因", 500, true);
            // This independent audit survives the cascade that removes file versions/events.
            db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(BusinessAttachment),
                Action = "DeleteReason",
                EntityId = id.ToString(CultureInfo.InvariantCulture),
                UserId = ActorName(actor),
                Timestamp = clock.UtcNow,
                OldValues = JsonSerializer.Serialize(new { item.InvoiceId, item.CategoryId, item.LatestRevision, item.CurrentRevision }),
                NewValues = JsonSerializer.Serialize(new { NoteLength = note.Length, NoteSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(note))) })
            });
            db.BusinessAttachments.Remove(item);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

    private void AddEvent(AppDbContext db, BusinessAttachment item, User actor, string action, string note) =>
        db.BusinessAttachmentEvents.Add(new BusinessAttachmentEvent
        {
            BusinessAttachmentId = item.Id,
            Action = action,
            Revision = item.CurrentRevision,
            ActorUserId = actor.Id,
            ActorName = ActorName(actor),
            Note = note,
            CreatedAt = clock.UtcNow
        });
}

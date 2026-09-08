using System.Security.Cryptography;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Attachments;

public sealed partial class BusinessAttachmentService(IDbContextFactory<AppDbContext> factory,
    BusinessDataAccessScope access, IBusinessClock clock, IRuntimePermissionAvailability runtime) : IBusinessAttachmentService
{
    private const string Resource = PermissionModuleCatalog.DocumentInvoices;

    public Task<BusinessAttachmentRecord> UploadAsync(int invoiceId, BusinessAttachmentUpload request, Stream content,
        CancellationToken cancellationToken = default) => RunAsync(false, async (db, actor, token) =>
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(content);
        Demand(PermissionAction.Operate, actor);
        await InvoiceAsync(db, invoiceId, actor, PermissionAction.Operate, false, token);
        if (request.UploadKey == Guid.Empty || request.AttachmentId is <= 0 || request.ExpectedVersion < 0 ||
            !Enum.IsDefined(request.Category)) throw new ServiceValidationException("附件编号、版本或分类无效。");
        string title = BusinessAttachmentFilePolicy.Text(request.Title, "资料名称", 200, true);
        string po = BusinessAttachmentFilePolicy.Text(request.PoNumber, "PO 号", 100);
        string style = BusinessAttachmentFilePolicy.Text(request.StyleNo, "款号", 200);
        string note = BusinessAttachmentFilePolicy.Text(request.Note, "版本说明", 500);
        string name = BusinessAttachmentFilePolicy.FileName(request.FileName);
        using var buffer = new MemoryStream();
        await BoundedStreamHelper.CopyToAsync(content, buffer, BusinessAttachmentLimits.FileBytes, token);
        byte[] bytes = buffer.ToArray();
        string type = BusinessAttachmentFilePolicy.Validate(name, bytes);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));

        return await AppDbContextExecution.ExecuteInTransactionAsync(factory, async (writeDb, writeToken) =>
        {
            // A database row lock serializes the per-invoice quota, including uploads
            // to different attachments. SQLite already serializes write transactions.
            await InvoiceAsync(writeDb, invoiceId, actor, PermissionAction.Operate, true, writeToken);
            var previous = await writeDb.BusinessAttachmentRevisions.AsNoTracking()
                .Where(item => item.UploadedByUserId == actor.Id && item.UploadKey == request.UploadKey)
                .Select(item => new { item.BusinessAttachmentId, item.Sha256, item.FileName, item.Note }).SingleOrDefaultAsync(writeToken);
            if (previous != null)
            {
                var existing = await writeDb.BusinessAttachments.SingleAsync(item => item.Id == previous.BusinessAttachmentId, writeToken);
                if (existing.InvoiceId != invoiceId || request.AttachmentId.HasValue && request.AttachmentId != existing.Id ||
                    existing.Title != title || existing.Category != request.Category || existing.PoNumber != po || existing.StyleNo != style ||
                    previous.Sha256 != hash || previous.FileName != name || previous.Note != note)
                    throw new ResourceConflictException("上传标识已用于其他资料，请重新选择文件后上传。");
                return await RecordAsync(writeDb, existing.Id, actor, writeToken);
            }

            BusinessAttachment attachment;
            if (request.AttachmentId is int attachmentId)
            {
                attachment = await writeDb.BusinessAttachments.SingleOrDefaultAsync(item => item.Id == attachmentId && item.InvoiceId == invoiceId, writeToken)
                    ?? throw new ResourceNotFoundException("业务资料不存在。");
                Version(request.ExpectedVersion, attachment.VersionNumber);
                if (attachment.IsArchived) throw new ResourceConflictException("请先恢复已停用的资料，再上传新版本。");
                if (attachment.Title != title || attachment.Category != request.Category || attachment.PoNumber != po || attachment.StyleNo != style)
                    throw new ServiceValidationException("新版本须沿用原资料的名称、分类、PO 号和款号。");
            }
            else
            {
                if (request.ExpectedVersion != 0) throw new ServiceValidationException("新资料不能携带已有版本号。");
                if (await writeDb.BusinessAttachments.CountAsync(item => item.InvoiceId == invoiceId, writeToken) >= BusinessAttachmentLimits.AttachmentsPerInvoice)
                    throw new ResourceConflictException("单张发票最多归档 100 份资料。");
                attachment = new BusinessAttachment { InvoiceId = invoiceId, Title = title, Category = request.Category, PoNumber = po, StyleNo = style };
                writeDb.BusinessAttachments.Add(attachment);
            }
            if (attachment.LatestRevision >= BusinessAttachmentLimits.RevisionsPerAttachment)
                throw new ResourceConflictException("每份资料最多保留 50 个版本。");
            long used = await (from revision in writeDb.BusinessAttachmentRevisions
                               join item in writeDb.BusinessAttachments on revision.BusinessAttachmentId equals item.Id
                               where item.InvoiceId == invoiceId
                               select (long?)revision.Length).SumAsync(writeToken) ?? 0;
            if (used + bytes.Length > BusinessAttachmentLimits.InvoiceBytes)
                throw new ResourceConflictException("该单据的归档资料已达到 256 MiB 总容量限制（包含旧版本和停用资料）。");

            attachment.LatestRevision++;
            attachment.UpdatedAt = clock.UtcNow;
            attachment.Revisions.Add(new BusinessAttachmentRevision
            {
                Revision = attachment.LatestRevision,
                UploadKey = request.UploadKey,
                FileName = name,
                ContentType = type,
                Length = bytes.Length,
                Sha256 = hash,
                Content = bytes,
                UploadedByUserId = actor.Id,
                UploadedBy = ActorName(actor),
                Note = note,
                CreatedAt = clock.UtcNow
            });
            await writeDb.SaveChangesAsync(writeToken);
            return await RecordAsync(writeDb, attachment.Id, actor, writeToken);
        }, token);
    }, cancellationToken);

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
            db.BusinessAttachmentEvents.Add(new BusinessAttachmentEvent
            {
                BusinessAttachmentId = id,
                Action = action,
                Revision = item.CurrentRevision,
                ActorUserId = actor.Id,
                ActorName = ActorName(actor),
                Note = note,
                CreatedAt = clock.UtcNow
            });
            await db.SaveChangesAsync(token);
            return await RecordAsync(db, id, actor, token);
        }, cancellationToken);

    private async Task<T> RunAsync<T>(bool write, Func<AppDbContext, User, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var actor = access.CurrentUser;
        if (actor is not { Id: > 0, IsActive: true }) throw new PermissionDeniedException("请先登录。");
        Demand(PermissionAction.View, actor);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (write) return await AppDbContextExecution.ExecuteInTransactionAsync(factory, (db, token) => operation(db, actor, token), timeout.Token);
            await using var db = await factory.CreateDbContextAsync(timeout.Token);
            return await operation(db, actor, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new ServiceTimeoutException("业务资料操作超时，请刷新后核对结果。", ex); }
        catch (DbUpdateConcurrencyException ex)
        { throw new ServiceConcurrencyException("资料已更新，请刷新后重试。", ex); }
        catch (Exception ex) when (RelationalExceptionClassifier.IsWriteContention(ex))
        { throw new ServiceConcurrencyException("资料正在被处理，请刷新后重试。", ex); }
        catch (DbUpdateException ex) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(ex))
        { throw new ResourceConflictException("该上传已提交，请刷新后核对结果。", ex); }
    }

    private void Demand(string action, User actor)
    {
        if (!runtime.IsPermissionAvailable(Resource, action)) throw new PermissionDeniedException("当前产品版不支持单据业务资料。");
        access.DemandPermission(Resource, action, actor);
    }

    private static void Version(int expected, int actual)
    {
        if (expected <= 0 || expected != actual) throw new ServiceConcurrencyException("资料已更新，请刷新后重试。");
    }

    private static string ActorName(User actor) => string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username : actor.FullName;

    private async Task<Invoice> InvoiceAsync(AppDbContext db, int id, User actor, string action, bool lockRow, CancellationToken token)
    {
        var query = lockRow && db.Database.IsNpgsql()
            ? db.Invoices.FromSqlInterpolated($"SELECT * FROM \"Invoices\" WHERE \"Id\" = {id} FOR UPDATE")
            : db.Invoices.Where(item => item.Id == id);
        var invoice = await query.AsNoTracking().SingleOrDefaultAsync(token) ?? throw new ResourceNotFoundException("发票不存在。");
        access.DemandRecordAccess(invoice, Resource, PermissionAction.View);
        access.DemandRecordAccess(invoice, Resource, action);
        return invoice;
    }

    private async Task<BusinessAttachment> AttachmentAsync(AppDbContext db, int id, User actor, string action, CancellationToken token)
    {
        var item = await db.BusinessAttachments.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new ResourceNotFoundException("业务资料不存在。");
        await InvoiceAsync(db, item.InvoiceId, actor, action, false, token);
        return item;
    }
}

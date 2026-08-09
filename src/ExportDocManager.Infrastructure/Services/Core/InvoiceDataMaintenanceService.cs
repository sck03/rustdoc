using System.Globalization;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public sealed class InvoiceDataMaintenanceService : IInvoiceDataMaintenanceService
    {
        private const string PurgeStoragePolicy =
            "发票数据清理是独立的管理员维护操作：只允许清理已作废发票，必须再次核对发票号并填写原因；操作在同一数据库事务中写入 MaintenancePurge 审计记录并删除发票、明细、状态历史及关联单一窗口工作区记录，不读取付款/报销业务表，也不创建系统盘文件。";

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ICurrentUserContext _currentUserContext;

        public InvoiceDataMaintenanceService(
            IDbContextFactory<AppDbContext> contextFactory,
            ICurrentUserContext currentUserContext)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        }

        public async Task<InvoiceDataMaintenancePreview> GetPurgePreviewAsync(
            int invoiceId,
            CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();
            if (invoiceId <= 0)
            {
                return null;
            }

            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var invoice = await context.Invoices
                .AsNoTracking()
                .Where(item => item.Id == invoiceId)
                .Select(item => new
                {
                    item.Id,
                    item.InvoiceNo,
                    item.Type,
                    item.Status,
                    item.InvoiceDate,
                    item.CustomerNameEN
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (invoice == null)
            {
                return null;
            }

            string status = InvoiceStatusCatalog.Normalize(invoice.Status);
            bool canPurge = InvoiceStatusCatalog.IsCancelled(status);
            return new InvoiceDataMaintenancePreview(
                invoice.Id,
                invoice.InvoiceNo?.Trim() ?? string.Empty,
                invoice.Type?.Trim() ?? string.Empty,
                status,
                InvoiceStatusCatalog.GetDisplayName(status),
                invoice.InvoiceDate,
                invoice.CustomerNameEN?.Trim() ?? string.Empty,
                canPurge,
                GetGuidance(status),
                PurgeStoragePolicy);
        }

        public async Task<InvoicePurgeResult> PurgeCancelledInvoiceAsync(
            InvoicePurgeCommand command,
            CancellationToken cancellationToken = default)
        {
            EnsureAdministrator();
            ArgumentNullException.ThrowIfNull(command);
            if (command.InvoiceId <= 0)
            {
                throw new InvoiceValidationException("发票 ID 必须大于 0。");
            }

            string confirmation = command.InvoiceNoConfirmation?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(confirmation))
            {
                throw new InvoiceValidationException("请输入完整发票号进行二次确认。");
            }

            string reason = command.Reason?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvoiceValidationException("请填写数据清理原因。");
            }

            if (reason.Length > 500)
            {
                throw new InvoiceValidationException("数据清理原因不能超过 500 个字符。");
            }

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        var invoice = await context.Invoices
                            .Include(item => item.Items)
                            .FirstOrDefaultAsync(item => item.Id == command.InvoiceId, token)
                            .ConfigureAwait(false);
                        if (invoice == null)
                        {
                            return null;
                        }

                        string status = InvoiceStatusCatalog.Normalize(invoice.Status);
                        if (!InvoiceStatusCatalog.IsCancelled(status))
                        {
                            throw new InvoiceConflictException(
                                status == InvoiceStatusCatalog.Draft
                                    ? "草稿发票应在发票编辑页使用普通删除，不允许通过管理员数据维护绕过正常流程。"
                                    : "只有已作废发票可以通过管理员数据维护清理；正式状态发票必须先作废。");
                        }

                        string invoiceNo = invoice.InvoiceNo?.Trim() ?? string.Empty;
                        if (!string.Equals(invoiceNo, confirmation, StringComparison.Ordinal))
                        {
                            throw new InvoiceValidationException("发票号确认不一致，未执行数据清理。");
                        }

                        await InvoiceDeletionSupport
                            .TrackSingleWindowWorkspaceDeletionAsync(context, invoice.Id, token)
                            .ConfigureAwait(false);

                        context.AuditLogs.Add(CreateMaintenanceAuditLog(
                            invoice,
                            reason,
                            _currentUserContext.CurrentUser?.Username));
                        context.Invoices.Remove(invoice);
                        await context.SaveChangesAsync(token).ConfigureAwait(false);

                        return new InvoicePurgeResult(
                            true,
                            invoice.Id,
                            invoiceNo,
                            status,
                            $"已清理已作废发票“{invoiceNo}”，维护原因和原始摘要已写入审计日志。",
                            PurgeStoragePolicy);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvoiceConflictException("数据清理失败：该发票已被其他用户修改或删除，请重新查询后再试。");
            }
        }

        private void EnsureAdministrator()
        {
            if (!BusinessDataAccessScope.CanViewAllBusinessData(_currentUserContext.CurrentUser))
            {
                throw new PermissionDeniedException("只有管理员可以使用发票数据清理功能。");
            }
        }

        private static string GetGuidance(string status)
        {
            if (InvoiceStatusCatalog.IsCancelled(status))
            {
                return "该发票已作废。仅在确有法规、测试数据或错误数据清理依据时，才可由管理员物理清理。";
            }

            if (string.Equals(status, InvoiceStatusCatalog.Draft, StringComparison.OrdinalIgnoreCase))
            {
                return "该发票仍为草稿，请回到发票编辑页使用普通删除。";
            }

            return "该发票属于正式业务状态，禁止物理删除；如确需清理，必须先按业务流程作废。";
        }

        private static AuditLog CreateMaintenanceAuditLog(
            Invoice invoice,
            string reason,
            string username)
        {
            return new AuditLog
            {
                EntityName = nameof(Invoice),
                Action = "MaintenancePurge",
                EntityId = invoice.Id.ToString(CultureInfo.InvariantCulture),
                OldValues = JsonSerializer.Serialize(new
                {
                    invoice.Id,
                    InvoiceNo = invoice.InvoiceNo?.Trim() ?? string.Empty,
                    Type = invoice.Type?.Trim() ?? string.Empty,
                    Status = InvoiceStatusCatalog.Normalize(invoice.Status),
                    invoice.OwnerUserId,
                    ItemCount = invoice.Items?.Count ?? 0
                }),
                NewValues = JsonSerializer.Serialize(new
                {
                    Reason = reason,
                    Policy = "Administrator cancelled-invoice purge"
                }),
                UserId = string.IsNullOrWhiteSpace(username) ? "Api" : username.Trim(),
                Timestamp = DateTime.UtcNow
            };
        }
    }
}

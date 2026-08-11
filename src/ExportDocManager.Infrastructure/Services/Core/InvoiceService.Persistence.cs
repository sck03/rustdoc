using System;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Core
{
    public partial class InvoiceService
    {
        public async Task<bool> DeleteInvoiceAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                return false;
            }

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        var invoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(
                                context.Invoices.Include(item => item.Items))
                            .FirstOrDefaultAsync(item => item.Id == id, token);
                        if (invoice == null)
                        {
                            return false;
                        }

                        if (!InvoiceStatusCatalog.IsEditable(invoice.Status))
                        {
                            throw new InvoiceConflictException(
                                "只有草稿发票可以直接删除。已核对、已出运或已结汇发票只能作废；已作废发票必须保留审计记录，如确需清理请使用管理员数据维护功能。");
                        }

                        await InvoiceDeletionSupport.TrackSingleWindowWorkspaceDeletionAsync(
                            context,
                            id,
                            token);

                        context.Invoices.Remove(invoice);
                        await context.SaveChangesAsync(token);
                        return true;
                    },
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvoiceConflictException("删除失败：该发票数据已被其他用户修改或删除，请刷新后重试。");
            }
            catch (InvoiceConflictException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InfrastructureServiceException("发票删除服务暂时不可用，请稍后重试。", ex);
            }
        }

        public async Task<Invoice> TransitionInvoiceStatusAsync(
            InvoiceStatusTransitionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.InvoiceId <= 0)
            {
                return null;
            }

            ValidateExpectedRowVersion(request.ExpectedRowVersion);
            string targetStatus = InvoiceStatusCatalog.Normalize(request.TargetStatus);
            string note = NormalizeStatusNote(request.Note, targetStatus == InvoiceStatusCatalog.Cancelled);

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        var invoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.Include(item => item.Items))
                            .FirstOrDefaultAsync(item => item.Id == request.InvoiceId, token);
                        if (invoice == null)
                        {
                            return null;
                        }

                        if (!invoice.RowVersion.SequenceEqual(request.ExpectedRowVersion))
                        {
                            throw new DbUpdateConcurrencyException();
                        }

                        if (!InvoiceStatusCatalog.IsKnown(targetStatus) ||
                            !InvoiceStatusCatalog.CanTransition(invoice.Status, targetStatus))
                        {
                            throw new InvoiceValidationException(
                                $"发票不能从“{InvoiceStatusCatalog.GetDisplayName(invoice.Status)}”直接流转到“{InvoiceStatusCatalog.GetDisplayName(targetStatus)}”。");
                        }

                        await InvoiceBusinessValidator.ValidateNormalizeAndCalculateAsync(
                            context,
                            invoice,
                            invoice.Items,
                            isNew: false,
                            existingStatus: invoice.Status,
                            token).ConfigureAwait(false);
                        InvoiceBusinessValidator.ValidateForStatusTransition(invoice, targetStatus);

                        string fromStatus = invoice.Status;
                        invoice.Status = targetStatus;
                        await PopulateMissingInvoiceSnapshotsAsync(context, invoice, token);
                        await AddStatusHistoryAsync(
                            context,
                            invoice.Id,
                            fromStatus,
                            targetStatus,
                            note,
                            token);
                        await context.SaveChangesAsync(token);

                        return invoice;
                    },
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvoiceConflictException("状态流转失败：该发票已被其他用户修改，请刷新后重试。");
            }
        }

        public async Task<Invoice> UnverifyInvoiceAsync(
            int id,
            byte[] expectedRowVersion,
            string note,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                return null;
            }

            ValidateExpectedRowVersion(expectedRowVersion);
            string normalizedNote = NormalizeStatusNote(note, required: true);

            try
            {
                return await AppDbContextExecution.ExecuteInTransactionAsync(
                    _contextFactory,
                    async (context, token) =>
                    {
                        var invoice = await _businessDataAccessScope
                            .ApplyInvoiceScope(context.Invoices.Include(item => item.Items))
                            .FirstOrDefaultAsync(item => item.Id == id, token);
                        if (invoice == null)
                        {
                            return null;
                        }

                        if (!invoice.RowVersion.SequenceEqual(expectedRowVersion))
                        {
                            throw new DbUpdateConcurrencyException();
                        }

                        if (InvoiceStatusCatalog.IsCancelled(invoice.Status))
                        {
                            throw new InvoiceValidationException(
                                "已作废发票不能反审核回到草稿；记录应继续保留，如确需物理清理请使用管理员数据维护功能。");
                        }

                        if (!InvoiceStatusCatalog.CanUnverify(invoice.Status))
                        {
                            throw new InvoiceValidationException("当前发票不是已锁定状态，无需反审核。");
                        }

                        string fromStatus = invoice.Status;
                        invoice.Status = InvoiceStatusCatalog.Draft;
                        await PopulateMissingInvoiceSnapshotsAsync(context, invoice, token);
                        await AddStatusHistoryAsync(
                            context,
                            invoice.Id,
                            fromStatus,
                            InvoiceStatusCatalog.Draft,
                            normalizedNote,
                            token);
                        await context.SaveChangesAsync(token);

                        return invoice;
                    },
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvoiceConflictException("反审核失败：该发票已被其他用户修改，请刷新后重试。");
            }
        }

        public async Task<IReadOnlyList<InvoiceStatusHistory>> ListInvoiceStatusHistoryAsync(
            int invoiceId,
            CancellationToken cancellationToken = default)
        {
            if (invoiceId <= 0)
            {
                return [];
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (!await _businessDataAccessScope
                    .CanAccessInvoiceAsync(context, invoiceId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return [];
            }

            return await context.InvoiceStatusHistories
                .AsNoTracking()
                .Where(item => item.InvoiceId == invoiceId)
                .OrderByDescending(item => item.ChangedAt)
                .ThenByDescending(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task SaveInvoiceCoreAsync(
            AppDbContext context,
            Invoice invoice,
            IReadOnlyList<HsCodeKnowledgeFeedbackInput> pendingHsFeedback,
            bool requireRowVersion = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(invoice);

            NormalizeInvoiceDates(invoice);
            var items = invoice.Items ?? [];
            bool isNew = invoice.Id == 0;
            string existingStatus = InvoiceStatusCatalog.Draft;

            _businessDataAccessScope.ApplyOwner(invoice);
            if (!isNew)
            {
                if (requireRowVersion && (invoice.RowVersion == null || invoice.RowVersion.Length == 0))
                {
                    throw new InvoiceValidationException("更新发票必须提交版本号，请刷新后重试。");
                }

                if (!await _businessDataAccessScope.CanAccessInvoiceAsync(
                        context,
                        invoice.Id,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new PermissionDeniedException("无权限修改该发票。");
                }

                existingStatus = await _businessDataAccessScope
                    .ApplyInvoiceScope(context.Invoices.AsNoTracking())
                    .Where(item => item.Id == invoice.Id)
                    .Select(item => item.Status)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!InvoiceStatusCatalog.IsEditable(existingStatus))
                {
                    throw new InvoiceConflictException("当前发票已锁定，请先反审核后再编辑。");
                }
            }

            await InvoiceBusinessValidator.ValidateNormalizeAndCalculateAsync(
                context,
                invoice,
                items,
                isNew,
                existingStatus,
                cancellationToken).ConfigureAwait(false);

            if (await context.Invoices.AsNoTracking().AnyAsync(item =>
                    item.Id != invoice.Id &&
                    item.CompanyScope == invoice.CompanyScope &&
                    item.InvoiceNo == invoice.InvoiceNo &&
                    item.Type == invoice.Type,
                    cancellationToken))
            {
                throw new InvoiceConflictException(
                    $"发票号“{invoice.InvoiceNo}”的{invoice.Type}已经存在，未覆盖原发票。请打开已有记录或使用复制功能创建新单号。");
            }

            items = invoice.Items;
            invoice.Items = null;

            try
            {
                await PopulateMissingInvoiceSnapshotsAsync(context, invoice, cancellationToken);
                if (invoice.Id > 0)
                {
                    context.Invoices.Update(invoice);
                }
                else
                {
                    await context.Invoices.AddAsync(invoice, cancellationToken);
                }

                await context.SaveChangesAsync(cancellationToken);

                if (items != null)
                {
                    await _itemService.SaveItemsAsync(context, invoice.Id, items, cancellationToken);
                }

                try
                {
                    await HsCodeKnowledgeFeedbackWriter.RecordInvoiceFeedbackAsync(
                        context,
                        items,
                        pendingHsFeedback,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    throw new InvoiceValidationException($"HS 编码确认记录无效：{ex.Message}");
                }
            }
            finally
            {
                invoice.Items = items;
            }
        }

        private async Task AddStatusHistoryAsync(
            AppDbContext context,
            int invoiceId,
            string fromStatus,
            string toStatus,
            string note,
            CancellationToken cancellationToken)
        {
            var currentUser = _businessDataAccessScope.CurrentUser;
            await context.InvoiceStatusHistories.AddAsync(new InvoiceStatusHistory
            {
                InvoiceId = invoiceId,
                FromStatus = InvoiceStatusCatalog.Normalize(fromStatus),
                ToStatus = InvoiceStatusCatalog.Normalize(toStatus),
                Note = note,
                ChangedByUserId = currentUser?.Id > 0 ? currentUser.Id : null,
                ChangedByUsername = currentUser?.Username?.Trim() ?? string.Empty,
                ChangedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        private static void ValidateExpectedRowVersion(byte[] expectedRowVersion)
        {
            if (expectedRowVersion == null || expectedRowVersion.Length == 0)
            {
                throw new InvoiceValidationException("状态操作必须提交发票版本号，请刷新后重试。");
            }
        }

        private static string NormalizeStatusNote(string note, bool required)
        {
            string normalized = note?.Trim() ?? string.Empty;
            if (required && string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvoiceValidationException("请填写状态变更原因。");
            }

            if (normalized.Length > 500)
            {
                throw new InvoiceValidationException("状态变更说明不能超过 500 个字符。");
            }

            return normalized;
        }

        private static async Task PopulateMissingInvoiceSnapshotsAsync(
            AppDbContext context,
            Invoice invoice,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(invoice);

            if (invoice.CustomerId > 0 && HasMissingCustomerSnapshot(invoice))
            {
                var customer = await context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.CustomerId, cancellationToken);

                if (customer != null)
                {
                    invoice.CustomerNameEN = PreferExistingValue(invoice.CustomerNameEN, customer.CustomerNameEN);
                    invoice.CustomerAddressEN = PreferExistingValue(invoice.CustomerAddressEN, customer.AddressEN);
                    invoice.NotifyPartyName = PreferExistingValue(invoice.NotifyPartyName, customer.NotifyPartyName);
                    invoice.NotifyPartyAddress = PreferExistingValue(invoice.NotifyPartyAddress, customer.NotifyPartyAddress);
                }
            }

            if (invoice.ExporterId > 0 && HasMissingExporterSnapshot(invoice))
            {
                var exporter = await context.Exporters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == invoice.ExporterId, cancellationToken);

                if (exporter != null)
                {
                    invoice.ExporterNameEN = PreferExistingValue(invoice.ExporterNameEN, exporter.ExporterNameEN);
                    invoice.ExporterNameCN = PreferExistingValue(invoice.ExporterNameCN, exporter.ExporterNameCN);
                    invoice.ExporterAddressEN = PreferExistingValue(invoice.ExporterAddressEN, exporter.AddressEN);
                    invoice.ExporterAddressCN = PreferExistingValue(invoice.ExporterAddressCN, exporter.AddressCN);
                    invoice.ExporterCreditCode = PreferExistingValue(invoice.ExporterCreditCode, exporter.CreditCode);
                    invoice.ExporterCustomsCode = PreferExistingValue(invoice.ExporterCustomsCode, exporter.CustomsCode);
                    invoice.BankName = PreferExistingValue(invoice.BankName, exporter.BankName);
                    invoice.BankAccount = PreferExistingValue(invoice.BankAccount, exporter.BankAccount);
                    invoice.SwiftCode = PreferExistingValue(invoice.SwiftCode, exporter.SwiftCode);
                }
            }
        }

        private static bool HasMissingCustomerSnapshot(Invoice invoice)
        {
            return string.IsNullOrWhiteSpace(invoice.CustomerNameEN) ||
                   string.IsNullOrWhiteSpace(invoice.CustomerAddressEN) ||
                   string.IsNullOrWhiteSpace(invoice.NotifyPartyName) ||
                   string.IsNullOrWhiteSpace(invoice.NotifyPartyAddress);
        }

        private static bool HasMissingExporterSnapshot(Invoice invoice)
        {
            return string.IsNullOrWhiteSpace(invoice.ExporterNameEN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterNameCN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterAddressEN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterAddressCN) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterCreditCode) ||
                   string.IsNullOrWhiteSpace(invoice.ExporterCustomsCode) ||
                   string.IsNullOrWhiteSpace(invoice.BankName) ||
                   string.IsNullOrWhiteSpace(invoice.BankAccount) ||
                   string.IsNullOrWhiteSpace(invoice.SwiftCode);
        }

        private static string PreferExistingValue(string currentValue, string fallbackValue)
        {
            return string.IsNullOrWhiteSpace(currentValue) ? fallbackValue : currentValue;
        }

        private static void NormalizeInvoiceDates(Invoice invoice)
        {
            invoice.InvoiceDate = DateTimeValueHelper.NormalizeBusinessDate(invoice.InvoiceDate);
            invoice.ShipmentDate = DateTimeValueHelper.NormalizeBusinessDate(invoice.ShipmentDate, invoice.InvoiceDate);
        }

    }
}

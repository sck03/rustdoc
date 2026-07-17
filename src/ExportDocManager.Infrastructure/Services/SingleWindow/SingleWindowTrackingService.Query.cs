using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService
    {
        public async Task<IReadOnlyList<SingleWindowOperationCenterRow>> QueryAsync(
            SingleWindowOperationCenterQuery query,
            CancellationToken cancellationToken = default)
        {
            query ??= new SingleWindowOperationCenterQuery();

            var page = await QueryPageAsync(
                new SingleWindowOperationCenterPageQuery
                {
                    BusinessType = query.BusinessType,
                    Status = query.Status,
                    Keyword = query.Keyword,
                    PageNumber = 1,
                    PageSize = NormalizeTake(query.Take)
                },
                cancellationToken);

            return page.Rows;
        }

        public async Task<SingleWindowOperationCenterPageResult> QueryPageAsync(
            SingleWindowOperationCenterPageQuery query,
            CancellationToken cancellationToken = default)
        {
            query ??= new SingleWindowOperationCenterPageQuery();
            var normalizedQuery = NormalizePageQuery(query);

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batches = _businessDataAccessScope.ApplySubmissionBatchScope(
                context.SwSubmissionBatches.AsNoTracking(),
                context);

            string businessType = normalizedQuery.BusinessType;
            if (!string.IsNullOrWhiteSpace(businessType))
            {
                batches = batches.Where(batch => batch.BusinessType == businessType);
            }

            string status = normalizedQuery.Status;
            if (!string.IsNullOrWhiteSpace(status))
            {
                batches = batches.Where(batch => batch.Status == status);
            }

            batches = ApplyKeywordFilter(batches, normalizedQuery.Keyword);

            int totalCount = await batches.CountAsync(cancellationToken);
            var pagedRows = await batches
                .OrderByDescending(batch => batch.UpdatedAt)
                .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
                .Take(normalizedQuery.PageSize)
                .Select(batch => new SingleWindowOperationCenterRow
                {
                    BatchId = batch.Id,
                    BatchReference = batch.BatchReference,
                    SubmissionVersion = batch.SubmissionVersion,
                    DraftRevision = batch.DraftRevision,
                    BusinessType = batch.BusinessType,
                    InvoiceNo = batch.InvoiceNo,
                    ContractNo = batch.ContractNo,
                    Status = batch.Status,
                    ReferenceNo = batch.ReferenceNo,
                    LastReceiptCode = batch.LastReceiptCode,
                    LastReceiptMessage = batch.LastReceiptMessage,
                    CreatedOnMachine = batch.CreatedOnMachine,
                    SubmitPackagePath = batch.SubmitPackagePath,
                    ClientProfileName = batch.ClientProfileName,
                    ClientDispatchPath = batch.ClientDispatchPath,
                    CreatedAt = batch.CreatedAt,
                    UpdatedAt = batch.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            if (pagedRows.Count > 0)
            {
                var batchIds = pagedRows.Select(row => row.BatchId).ToList();
                var receiptCounts = await context.SwReceiptLogs
                    .AsNoTracking()
                    .Where(log => batchIds.Contains(log.BatchId))
                    .GroupBy(log => log.BatchId)
                    .Select(group => new
                    {
                        BatchId = group.Key,
                        Count = group.Count()
                    })
                    .ToDictionaryAsync(item => item.BatchId, item => item.Count, cancellationToken);

                pagedRows = pagedRows
                    .Select(row => CloneRowWithReceiptCount(row, receiptCounts.TryGetValue(row.BatchId, out var count) ? count : 0))
                    .ToList();
            }

            return new SingleWindowOperationCenterPageResult
            {
                Rows = pagedRows,
                TotalCount = totalCount,
                PageNumber = normalizedQuery.PageNumber,
                PageSize = normalizedQuery.PageSize
            };
        }

        public async Task<SingleWindowOperationCenterDetail> GetDetailAsync(
            int batchId,
            CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches.AsNoTracking(), context)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken)
                ?? throw new InvalidOperationException("未找到指定的单一窗口批次。");

            var packageRecords = await context.SwHandoffPackageRecords
                .AsNoTracking()
                .Where(item => item.BatchId == batchId)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new SingleWindowOperationCenterPackageRecord
                {
                    PackageType = item.PackageType,
                    Direction = item.Direction,
                    FilePath = item.FilePath,
                    CreatedOnMachine = item.CreatedOnMachine,
                    PayloadFileCount = item.PayloadFileCount,
                    AttachmentFileCount = item.AttachmentFileCount,
                    WarningCount = item.WarningCount,
                    CreatedAt = item.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var receiptRecords = await context.SwReceiptLogs
                .AsNoTracking()
                .Where(item => item.BatchId == batchId)
                .OrderByDescending(item => item.ImportedAt)
                .Select(item => new SingleWindowOperationCenterReceiptRecord
                {
                    ReceiptKind = item.ReceiptKind,
                    ReferenceNo = item.ReferenceNo,
                    ReceiptCode = item.ReceiptCode,
                    ReceiptMessage = item.ReceiptMessage,
                    BusinessStatus = item.BusinessStatus,
                    SourceFileName = item.SourceFileName,
                    ImportedAt = item.ImportedAt,
                    OccurredAt = item.OccurredAt
                })
                .ToListAsync(cancellationToken);

            return new SingleWindowOperationCenterDetail
            {
                BatchId = batch.Id,
                BatchReference = batch.BatchReference,
                SubmissionVersion = batch.SubmissionVersion,
                DraftRevision = batch.DraftRevision,
                BusinessType = batch.BusinessType,
                InvoiceNo = batch.InvoiceNo,
                ContractNo = batch.ContractNo,
                Status = batch.Status,
                ReferenceNo = batch.ReferenceNo,
                SubmitPackagePath = batch.SubmitPackagePath,
                LastReceiptPackagePath = batch.LastReceiptPackagePath,
                WorkingDirectoryPath = batch.WorkingDirectoryPath,
                ClientProfileName = batch.ClientProfileName,
                ClientDispatchPath = batch.ClientDispatchPath,
                CreatedOnMachine = batch.CreatedOnMachine,
                PayloadFileCount = batch.PayloadFileCount,
                AttachmentFileCount = batch.AttachmentFileCount,
                WarningCount = batch.WarningCount,
                CreatedAt = batch.CreatedAt,
                UpdatedAt = batch.UpdatedAt,
                LastReceiptAt = batch.LastReceiptAt,
                LastClientDispatchAt = batch.LastClientDispatchAt,
                PackageRecords = packageRecords,
                ReceiptRecords = receiptRecords
            };
        }

        private static IQueryable<SwSubmissionBatch> ApplyKeywordFilter(
            IQueryable<SwSubmissionBatch> batches,
            string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return batches;
            }

            return batches.ApplyKeywordSearch(
                keyword,
                batch => batch.InvoiceNo,
                batch => batch.ContractNo,
                batch => batch.BatchReference,
                batch => batch.ReferenceNo,
                batch => batch.LastReceiptCode,
                batch => batch.LastReceiptMessage,
                batch => batch.CreatedOnMachine,
                batch => batch.ClientProfileName);
        }

        private static int NormalizeTake(int take)
        {
            return Math.Clamp(take <= 0 ? 200 : take, 1, 500);
        }

        private static SingleWindowOperationCenterPageQuery NormalizePageQuery(SingleWindowOperationCenterPageQuery query)
        {
            return new SingleWindowOperationCenterPageQuery
            {
                BusinessType = TextSearchHelper.NormalizeFilter(query.BusinessType),
                Status = TextSearchHelper.NormalizeFilter(query.Status),
                Keyword = TextSearchHelper.NormalizeFilter(query.Keyword),
                PageNumber = Math.Max(1, query.PageNumber),
                PageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 200)
            };
        }

        private static SingleWindowOperationCenterRow CloneRowWithReceiptCount(SingleWindowOperationCenterRow row, int receiptCount)
        {
            return new SingleWindowOperationCenterRow
            {
                BatchId = row.BatchId,
                BatchReference = row.BatchReference,
                SubmissionVersion = row.SubmissionVersion,
                DraftRevision = row.DraftRevision,
                BusinessType = row.BusinessType,
                InvoiceNo = row.InvoiceNo,
                ContractNo = row.ContractNo,
                Status = row.Status,
                ReferenceNo = row.ReferenceNo,
                LastReceiptCode = row.LastReceiptCode,
                LastReceiptMessage = row.LastReceiptMessage,
                CreatedOnMachine = row.CreatedOnMachine,
                SubmitPackagePath = row.SubmitPackagePath,
                ClientProfileName = row.ClientProfileName,
                ClientDispatchPath = row.ClientDispatchPath,
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt,
                ReceiptCount = receiptCount
            };
        }
    }
}

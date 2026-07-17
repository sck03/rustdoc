using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public class AuditLogService : IAuditLogService
    {
        private const int DeleteBatchSize = 900;
        private static readonly IReadOnlyList<(string Header, double Width)> ExportColumns =
        [
            ("时间", 20),
            ("实体", 16),
            ("动作", 12),
            ("实体ID", 22),
            ("操作人", 16),
            ("变更前", 50),
            ("变更后", 50)
        ];

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IAuditLogReadRepository _auditLogReadRepository;

        public AuditLogService(
            IDbContextFactory<AppDbContext> contextFactory,
            IAuditLogReadRepository auditLogReadRepository)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _auditLogReadRepository = auditLogReadRepository ?? throw new ArgumentNullException(nameof(auditLogReadRepository));
        }

        public async Task<List<AuditLog>> QueryAsync(AuditLogQueryCriteria criteria, int maxCount = 2000)
        {
            var rows = await _auditLogReadRepository.QueryAllAsync(
                AuditLogQueryHelper.ToPageQuery(criteria),
                maxCount);
            return rows.ToList();
        }

        public async Task<int> ExportToExcelAsync(
            AuditLogQueryCriteria criteria,
            string filePath,
            int maxCount = 50000,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string destinationPath = Path.GetFullPath(filePath);
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var rows = await _auditLogReadRepository.QueryAllAsync(
                AuditLogQueryHelper.ToPageQuery(criteria),
                Math.Max(1, maxCount),
                cancellationToken);

            if (rows.Count == 0)
            {
                return 0;
            }

            await AtomicFileHelper.WriteFileAtomicAsync(
                destinationPath,
                (tempFilePath, ct) => Task.Run(
                    () => WriteAuditLogWorkbook(tempFilePath, rows, ct),
                    ct),
                cancellationToken);

            return rows.Count;
        }

        public async Task<byte[]> ExportToExcelBytesAsync(
            AuditLogQueryCriteria criteria,
            int maxCount = 50000,
            CancellationToken cancellationToken = default)
        {
            var rows = await _auditLogReadRepository.QueryAllAsync(
                AuditLogQueryHelper.ToPageQuery(criteria),
                Math.Max(1, maxCount),
                cancellationToken);

            await using var output = new MemoryStream();
            await Task.Run(
                () => WriteAuditLogWorkbook(output, rows, cancellationToken),
                cancellationToken);
            return output.ToArray();
        }

        public async Task<int> DeleteByCriteriaAsync(AuditLogQueryCriteria criteria, int maxCount = 50000)
        {
            var normalizedCriteria = AuditLogQueryHelper.ToPageQuery(criteria);
            return await DeleteInBatchesAsync(
                context => AuditLogQueryHelper.ApplyCriteria(context.AuditLogs.AsQueryable(), normalizedCriteria),
                maxCount);
        }

        public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int maxCount = 200000)
        {
            return await DeleteInBatchesAsync(
                context => context.AuditLogs.Where(x => x.Timestamp < cutoffUtc),
                maxCount);
        }

        private async Task<int> DeleteInBatchesAsync(
            Func<AppDbContext, IQueryable<AuditLog>> buildQuery,
            int maxCount)
        {
            ArgumentNullException.ThrowIfNull(buildQuery);

            int remaining = Math.Max(1, maxCount);
            int deletedCount = 0;

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    while (remaining > 0)
                    {
                        int batchSize = Math.Min(DeleteBatchSize, remaining);
                        var ids = await buildQuery(context)
                            .OrderBy(log => log.Timestamp)
                            .ThenBy(log => log.Id)
                            .Select(log => log.Id)
                            .Take(batchSize)
                            .ToListAsync();

                        if (ids.Count == 0)
                        {
                            break;
                        }

                        deletedCount += await context.AuditLogs
                            .Where(log => ids.Contains(log.Id))
                            .ExecuteDeleteAsync();

                        remaining -= ids.Count;
                    }

                    return deletedCount;
                });
        }

        private static void WriteAuditLogWorkbook(
            string filePath,
            IReadOnlyList<AuditLog> rows,
            CancellationToken cancellationToken)
        {
            using var output = File.Create(filePath);
            WriteAuditLogWorkbook(output, rows, cancellationToken);
        }

        private static void WriteAuditLogWorkbook(
            Stream output,
            IReadOnlyList<AuditLog> rows,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("AuditLogs");

            for (int index = 0; index < ExportColumns.Count; index++)
            {
                var column = ExportColumns[index];
                int columnNumber = index + 1;
                worksheet.Cell(1, columnNumber).Value = column.Header;
                worksheet.Cell(1, columnNumber).Style.Font.Bold = true;
                worksheet.Column(columnNumber).Width = column.Width;
            }

            int rowNumber = 2;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                worksheet.Cell(rowNumber, 1).Value = row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cell(rowNumber, 2).Value = row.EntityName ?? string.Empty;
                worksheet.Cell(rowNumber, 3).Value = row.Action ?? string.Empty;
                worksheet.Cell(rowNumber, 4).Value = row.EntityId ?? string.Empty;
                worksheet.Cell(rowNumber, 5).Value = row.UserId ?? string.Empty;
                worksheet.Cell(rowNumber, 6).Value = row.OldValues ?? string.Empty;
                worksheet.Cell(rowNumber, 7).Value = row.NewValues ?? string.Empty;
                rowNumber++;
            }

            worksheet.Range(1, 1, 1, ExportColumns.Count).SetAutoFilter();
            worksheet.SheetView.FreezeRows(1);
            workbook.SaveAs(output);
        }
    }
}

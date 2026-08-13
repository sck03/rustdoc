using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public class AuditLogService : IAuditLogService
    {
        private const int DeleteBatchSize = 900;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IAuditLogReadRepository _auditLogReadRepository;
        private readonly IAuditLogExcelExporter _excelExporter;

        public AuditLogService(
            IDbContextFactory<AppDbContext> contextFactory,
            IAuditLogReadRepository auditLogReadRepository,
            IAuditLogExcelExporter? excelExporter = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _auditLogReadRepository = auditLogReadRepository ?? throw new ArgumentNullException(nameof(auditLogReadRepository));
            _excelExporter = excelExporter ?? new UnsupportedAuditLogExcelExporter();
        }

        public async Task<List<AuditLog>> QueryAsync(
            AuditLogQueryCriteria criteria,
            int maxCount = 2000,
            CancellationToken cancellationToken = default)
        {
            var rows = await _auditLogReadRepository.QueryAllAsync(
                AuditLogQueryHelper.ToPageQuery(criteria),
                maxCount,
                cancellationToken);
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
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var rows = await _auditLogReadRepository.QueryAllAsync(
                AuditLogQueryHelper.ToPageQuery(criteria),
                Math.Max(1, maxCount),
                cancellationToken);

            await _excelExporter.ExportAsync(rows, destinationPath, cancellationToken);

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

            return await _excelExporter.ExportBytesAsync(rows, cancellationToken);
        }

        public async Task<int> DeleteByCriteriaAsync(
            AuditLogQueryCriteria criteria,
            int maxCount = 50000,
            CancellationToken cancellationToken = default)
        {
            var normalizedCriteria = AuditLogQueryHelper.ToPageQuery(criteria);
            return await DeleteInBatchesAsync(
                context => AuditLogQueryHelper.ApplyCriteria(context.AuditLogs.AsQueryable(), normalizedCriteria),
                maxCount,
                cancellationToken);
        }

        public async Task<int> DeleteOlderThanAsync(
            DateTimeOffset cutoffUtc,
            int maxCount = 200000,
            CancellationToken cancellationToken = default)
        {
            return await DeleteInBatchesAsync(
                context => context.AuditLogs.Where(x => x.Timestamp < cutoffUtc),
                maxCount,
                cancellationToken);
        }

        private async Task<int> DeleteInBatchesAsync(
            Func<AppDbContext, IQueryable<AuditLog>> buildQuery,
            int maxCount,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buildQuery);

            int remaining = Math.Max(1, maxCount);
            int deletedCount = 0;

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    while (remaining > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int batchSize = Math.Min(DeleteBatchSize, remaining);
                        var ids = await buildQuery(context)
                            .OrderBy(log => log.Timestamp)
                            .ThenBy(log => log.Id)
                            .Select(log => log.Id)
                            .Take(batchSize)
                            .ToListAsync(token);

                        if (ids.Count == 0)
                        {
                            break;
                        }

                        deletedCount += await context.AuditLogs
                            .Where(log => ids.Contains(log.Id))
                            .ExecuteDeleteAsync(token);

                        remaining -= ids.Count;
                    }

                    return deletedCount;
                },
                cancellationToken);
        }

    }
}

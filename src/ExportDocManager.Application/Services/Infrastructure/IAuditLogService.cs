using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Infrastructure
{
    public class AuditLogQueryCriteria
    {
        public string InvoiceKeyword { get; set; }
        public string EntityName { get; set; }
        public string Action { get; set; }
        public string UserId { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Keyword { get; set; }
    }

    public interface IAuditLogService
    {
        Task<List<AuditLog>> QueryAsync(AuditLogQueryCriteria criteria, int maxCount = 2000);
        Task<int> ExportToExcelAsync(
            AuditLogQueryCriteria criteria,
            string filePath,
            int maxCount = 50000,
            CancellationToken cancellationToken = default);
        Task<byte[]> ExportToExcelBytesAsync(
            AuditLogQueryCriteria criteria,
            int maxCount = 50000,
            CancellationToken cancellationToken = default);
        Task<int> DeleteByCriteriaAsync(AuditLogQueryCriteria criteria, int maxCount = 50000);
        Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int maxCount = 200000);
    }
}

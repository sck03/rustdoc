using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public static class AuditLogQueryHelper
    {
        public static AuditLogPageQuery ToPageQuery(AuditLogQueryCriteria criteria)
        {
            return new AuditLogPageQuery
            {
                InvoiceKeyword = TextSearchHelper.NormalizeFilter(criteria?.InvoiceKeyword),
                EntityName = NormalizeOption(criteria?.EntityName),
                Action = NormalizeOption(criteria?.Action),
                UserId = TextSearchHelper.NormalizeFilter(criteria?.UserId),
                StartTime = criteria?.StartTime,
                EndTime = criteria?.EndTime,
                Keyword = TextSearchHelper.NormalizeFilter(criteria?.Keyword)
            };
        }

        public static AuditLogQueryCriteria ToCriteria(AuditLogPageQuery query)
        {
            return new AuditLogQueryCriteria
            {
                InvoiceKeyword = EmptyToNull(TextSearchHelper.NormalizeFilter(query?.InvoiceKeyword)),
                EntityName = EmptyToNull(NormalizeOption(query?.EntityName)),
                Action = EmptyToNull(NormalizeOption(query?.Action)),
                UserId = EmptyToNull(TextSearchHelper.NormalizeFilter(query?.UserId)),
                StartTime = query?.StartTime,
                EndTime = query?.EndTime,
                Keyword = EmptyToNull(TextSearchHelper.NormalizeFilter(query?.Keyword))
            };
        }

        public static AuditLogPageQuery NormalizePageQuery(AuditLogPageQuery query)
        {
            query ??= new AuditLogPageQuery();
            return query with
            {
                InvoiceKeyword = TextSearchHelper.NormalizeFilter(query.InvoiceKeyword),
                EntityName = NormalizeOption(query.EntityName),
                Action = NormalizeOption(query.Action),
                UserId = TextSearchHelper.NormalizeFilter(query.UserId),
                Keyword = TextSearchHelper.NormalizeFilter(query.Keyword)
            };
        }

        public static IQueryable<AuditLog> ApplyCriteria(
            IQueryable<AuditLog> query,
            AuditLogPageQuery criteria)
        {
            ArgumentNullException.ThrowIfNull(query);
            criteria ??= new AuditLogPageQuery();

            var entityName = NormalizeOption(criteria.EntityName);
            if (!string.IsNullOrWhiteSpace(entityName))
            {
                query = query.Where(log => log.EntityName == entityName);
            }

            var action = NormalizeOption(criteria.Action);
            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(log => log.Action == action);
            }

            var userId = TextSearchHelper.NormalizeFilter(criteria.UserId);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(log => log.UserId != null && log.UserId.Contains(userId));
            }

            if (criteria.StartTime.HasValue)
            {
                query = query.Where(log => log.Timestamp >= criteria.StartTime.Value);
            }

            if (criteria.EndTime.HasValue)
            {
                query = query.Where(log => log.Timestamp <= criteria.EndTime.Value);
            }

            var invoiceKeyword = TextSearchHelper.NormalizeFilter(criteria.InvoiceKeyword);
            if (!string.IsNullOrWhiteSpace(invoiceKeyword))
            {
                query = query.Where(log =>
                    (log.EntityName == nameof(Invoice) &&
                     ((log.EntityId != null && log.EntityId.Contains(invoiceKeyword)) ||
                      (log.OldValues != null && log.OldValues.Contains(invoiceKeyword)) ||
                      (log.NewValues != null && log.NewValues.Contains(invoiceKeyword)))) ||
                    (log.OldValues != null && log.OldValues.Contains(invoiceKeyword)) ||
                    (log.NewValues != null && log.NewValues.Contains(invoiceKeyword)));
            }

            var keyword = TextSearchHelper.NormalizeFilter(criteria.Keyword);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(log =>
                    (log.EntityName != null && log.EntityName.Contains(keyword)) ||
                    (log.EntityId != null && log.EntityId.Contains(keyword)) ||
                    (log.UserId != null && log.UserId.Contains(keyword)) ||
                    (log.OldValues != null && log.OldValues.Contains(keyword)) ||
                    (log.NewValues != null && log.NewValues.Contains(keyword)));
            }

            return query;
        }

        public static string NormalizeOption(string value)
        {
            var normalized = TextSearchHelper.NormalizeFilter(value);
            return string.Equals(normalized, "全部", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
        }

        private static string EmptyToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}

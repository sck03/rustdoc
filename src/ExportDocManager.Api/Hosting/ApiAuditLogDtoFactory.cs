using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiAuditLogDtoFactory
    {
        public static ApiPagedResponse<ApiAuditLogDto> FromPagedAuditLogs(PagedResult<AuditLog> result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiPagedResponse<ApiAuditLogDto>(
                result.Items.Select(FromAuditLog).ToList(),
                result.TotalCount,
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage);
        }

        public static ApiAuditLogDto FromAuditLog(AuditLog auditLog)
        {
            ArgumentNullException.ThrowIfNull(auditLog);

            return new ApiAuditLogDto
            {
                Id = auditLog.Id,
                EntityName = auditLog.EntityName ?? string.Empty,
                Action = auditLog.Action ?? string.Empty,
                EntityId = auditLog.EntityId ?? string.Empty,
                OldValues = auditLog.OldValues ?? string.Empty,
                NewValues = auditLog.NewValues ?? string.Empty,
                UserId = auditLog.UserId ?? string.Empty,
                Timestamp = auditLog.Timestamp,
                OldValuesPreview = Shrink(auditLog.OldValues),
                NewValuesPreview = Shrink(auditLog.NewValues)
            };
        }

        private static string Shrink(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var plain = value.Replace("\r", " ").Replace("\n", " ");
            return plain.Length > 180 ? plain[..180] + "..." : plain;
        }
    }
}

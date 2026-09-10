using System.Globalization;
using System.Text.Json;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.DataAccess;

internal static class RecordDeletionAudit
{
    internal static void Add(AppDbContext db, User actor, string entityName, object id, string reason, DateTimeOffset timestamp) =>
        db.AuditLogs.Add(new AuditLog
        {
            EntityName = entityName,
            EntityId = Convert.ToString(id, CultureInfo.InvariantCulture)!,
            Action = "Delete",
            UserId = actor.Username,
            Timestamp = timestamp,
            NewValues = JsonSerializer.Serialize(new { Reason = AuditValuePolicy.Sanitize("Reason", reason) })
        });
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.DataAccess
{
    public class AuditInterceptor : SaveChangesInterceptor
    {
        private const string PendingEntityId = "[generated-after-save]";
        private readonly IAuditUserProvider _auditUserProvider;
        private readonly ConditionalWeakTable<DbContext, PendingAuditState> _pendingAudits = new();

        public AuditInterceptor()
            : this(new SystemAuditUserProvider())
        {
        }

        public AuditInterceptor(IAuditUserProvider auditUserProvider)
        {
            _auditUserProvider = auditUserProvider ?? throw new ArgumentNullException(nameof(auditUserProvider));
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            var context = eventData.Context;
            if (context == null) return base.SavingChanges(eventData, result);

            UpdateRowVersions(context);
            var auditEntries = PrepareAuditLog(context);
            if (auditEntries.Count > 0)
            {
                context.Set<AuditLog>().AddRange(auditEntries);
            }

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context != null)
            {
                UpdateRowVersions(context);
                var auditEntries = PrepareAuditLog(context);
                if (auditEntries.Count > 0)
                {
                    context.Set<AuditLog>().AddRange(auditEntries);
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            FinalizePendingEntityIds(eventData.Context);
            return base.SavedChanges(eventData, result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            await FinalizePendingEntityIdsAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
            return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            RemovePendingAuditState(eventData.Context);
            base.SaveChangesFailed(eventData);
        }

        public override async Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            RemovePendingAuditState(eventData.Context);
            await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
        }

        private static void UpdateRowVersions(DbContext context)
        {
            foreach (var entry in context.ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                var rowVersionProperty = entry.Properties.FirstOrDefault(property =>
                    property.Metadata.Name == "RowVersion" &&
                    property.Metadata.ClrType == typeof(byte[]));

                if (rowVersionProperty == null)
                {
                    continue;
                }

                rowVersionProperty.CurrentValue = CreateRowVersion();
                if (entry.State == EntityState.Modified)
                {
                    rowVersionProperty.IsModified = true;
                }
            }
        }

        private static byte[] CreateRowVersion()
        {
            var buffer = new byte[8];
            RandomNumberGenerator.Fill(buffer);
            return buffer;
        }

        private List<AuditLog> PrepareAuditLog(DbContext context)
        {
            var auditEntries = new List<AuditLog>();
            var entries = context.ChangeTracker.Entries()
                .Where(e => e.Entity is not AuditLog && 
                           (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
                .ToList();

            foreach (var entry in entries)
            {
                var auditLog = new AuditLog
                {
                    EntityName = entry.Entity.GetType().Name,
                    Action = entry.State.ToString(),
                    Timestamp = DateTime.UtcNow,
                    UserId = _auditUserProvider.GetCurrentUserName()
                };

                var primaryKeys = entry.Properties
                    .Where(property => property.Metadata.IsPrimaryKey())
                    .ToArray();
                bool hasTemporaryKey = primaryKeys.Any(property => property.IsTemporary);
                // Handle complex or non-string primary keys gracefully. Database-generated
                // keys are finalized after the insert so audit rows never expose EF's
                // negative temporary key values.
                auditLog.EntityId = hasTemporaryKey
                    ? PendingEntityId
                    : FormatEntityId(primaryKeys);

                var oldValues = new Dictionary<string, object>();
                var newValues = new Dictionary<string, object>();

                foreach (var property in entry.Properties)
                {
                    // Skip temporary values and concurrency tokens like RowVersion
                    if (property.IsTemporary || property.Metadata.Name == "RowVersion")
                    {
                        continue;
                    }

                    string propertyName = property.Metadata.Name;

                    try 
                    {
                        switch (entry.State)
                        {
                            case EntityState.Added:
                                newValues[propertyName] = SanitizeAuditValue(propertyName, property.CurrentValue);
                                break;
                            case EntityState.Deleted:
                                oldValues[propertyName] = SanitizeAuditValue(propertyName, property.OriginalValue);
                                break;
                            case EntityState.Modified:
                                if (property.IsModified)
                                {
                                    oldValues[propertyName] = SanitizeAuditValue(propertyName, property.OriginalValue);
                                    newValues[propertyName] = SanitizeAuditValue(propertyName, property.CurrentValue);
                                }
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore properties that fail to serialize or read
                    }
                }

                // Use safe serialization options
                var jsonOptions = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
                
                try
                {
                    auditLog.OldValues = oldValues.Count == 0 ? null : JsonSerializer.Serialize(oldValues, jsonOptions);
                    auditLog.NewValues = newValues.Count == 0 ? null : JsonSerializer.Serialize(newValues, jsonOptions);
                }
                catch (Exception)
                {
                    // Fallback if serialization fails
                    auditLog.OldValues = "Serialization Error";
                    auditLog.NewValues = "Serialization Error";
                }

                auditEntries.Add(auditLog);
                if (hasTemporaryKey)
                {
                    _pendingAudits.GetOrCreateValue(context).Entries.Add(
                        new PendingAuditEntry(entry, auditLog));
                }
            }

            return auditEntries;
        }

        private void FinalizePendingEntityIds(DbContext context)
        {
            var state = TakePendingAuditState(context);
            if (state == null || state.Entries.Count == 0)
            {
                return;
            }

            foreach (var pending in state.Entries)
            {
                string entityId = FormatEntityId(
                    pending.SourceEntry.Properties
                        .Where(property => property.Metadata.IsPrimaryKey())
                        .ToArray());
                if (!string.Equals(entityId, "Unknown", StringComparison.Ordinal))
                {
                    pending.AuditLog.EntityId = entityId;
                }
            }

            PersistPendingAuditIds(context);
        }

        private async Task FinalizePendingEntityIdsAsync(
            DbContext context,
            CancellationToken cancellationToken)
        {
            var state = TakePendingAuditState(context);
            if (state == null || state.Entries.Count == 0)
            {
                return;
            }

            foreach (var pending in state.Entries)
            {
                string entityId = FormatEntityId(
                    pending.SourceEntry.Properties
                        .Where(property => property.Metadata.IsPrimaryKey())
                        .ToArray());
                if (!string.Equals(entityId, "Unknown", StringComparison.Ordinal))
                {
                    pending.AuditLog.EntityId = entityId;
                }
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The business save has already completed. Keep the explicit marker
                // rather than converting a successful request into a false failure.
            }
        }

        private static void PersistPendingAuditIds(DbContext context)
        {
            try
            {
                context.SaveChanges();
            }
            catch
            {
                // The original business save has completed. A visible marker is safer
                // than failing an otherwise successful operation during audit cleanup.
            }
        }

        private PendingAuditState TakePendingAuditState(DbContext context)
        {
            if (context == null || !_pendingAudits.TryGetValue(context, out var state))
            {
                return null;
            }

            _pendingAudits.Remove(context);
            return state;
        }

        private void RemovePendingAuditState(DbContext context)
        {
            if (context != null)
            {
                _pendingAudits.Remove(context);
            }
        }

        private static string FormatEntityId(IReadOnlyList<PropertyEntry> primaryKeys)
        {
            if (primaryKeys == null || primaryKeys.Count == 0)
            {
                return "Unknown";
            }

            if (primaryKeys.Any(property => property.CurrentValue == null || property.IsTemporary))
            {
                return "Unknown";
            }

            return string.Join(",", primaryKeys.Select(property => property.CurrentValue.ToString()));
        }

        private static object SanitizeAuditValue(string propertyName, object value)
        {
            if (value == null)
            {
                return null;
            }

            string name = propertyName ?? string.Empty;
            if (name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Token", StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED]";
            }

            if (value is string text &&
                (name.Equals("ContentHtml", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("BodyHtml", StringComparison.OrdinalIgnoreCase) ||
                 text.Length > 2048))
            {
                using var sha = SHA256.Create();
                string hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))[..16];
                return $"[TEXT length={text.Length} sha256={hash}]";
            }

            return value;
        }

        private sealed class PendingAuditState
        {
            public List<PendingAuditEntry> Entries { get; } = [];
        }

        private sealed record PendingAuditEntry(EntityEntry SourceEntry, AuditLog AuditLog);
    }
}

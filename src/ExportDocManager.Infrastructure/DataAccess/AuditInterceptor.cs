using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExportDocManager.DataAccess
{
    public class AuditInterceptor : SaveChangesInterceptor
    {
        private readonly IAuditUserProvider _auditUserProvider;

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

                var primaryKey = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
                // Handle complex or non-string primary keys gracefully
                auditLog.EntityId = primaryKey?.CurrentValue?.ToString() ?? "Unknown";

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
                    auditEntries.Add(auditLog);
                }
                catch (Exception)
                {
                    // Fallback if serialization fails
                    auditLog.OldValues = "Serialization Error";
                    auditLog.NewValues = "Serialization Error";
                    auditEntries.Add(auditLog);
                }
            }

            return auditEntries;
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
    }
}

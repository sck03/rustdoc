using System.Text;
using System.Text.Json;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Services.Infrastructure
{
    public static class ServerMigrationSecurityAudit
    {
        private static readonly object WriteGate = new();

        public static void Write(
            IAppPathProvider pathProvider,
            string action,
            ServerMigrationRequestContext requestContext,
            string packageId,
            bool? success,
            string message)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            requestContext ??= new ServerMigrationRequestContext(string.Empty, string.Empty);
            string auditRoot = Path.Combine(pathProvider.LogRoot, "Security");
            string auditPath = Path.Combine(auditRoot, "server-migration.jsonl");
            var record = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                action = action?.Trim() ?? string.Empty,
                requestedBy = requestContext.RequestedBy?.Trim() ?? string.Empty,
                remoteAddress = requestContext.RemoteAddress?.Trim() ?? string.Empty,
                packageId = packageId?.Trim() ?? string.Empty,
                success,
                message = Limit(message, 2_000)
            };
            string line = JsonSerializer.Serialize(record) + Environment.NewLine;

            lock (WriteGate)
            {
                Directory.CreateDirectory(auditRoot);
                RuntimeFilePermissionHelper.RestrictDirectory(auditRoot);
                File.AppendAllText(auditPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                RuntimeFilePermissionHelper.RestrictFile(auditPath);
            }
        }

        private static string Limit(string value, int maximumLength)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length <= maximumLength
                ? normalized
                : normalized[..maximumLength];
        }
    }
}

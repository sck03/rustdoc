using System.Text;
using System.Text.Json;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Services.Infrastructure
{
    public static class ServerMigrationSecurityAudit
    {
        private static readonly Lock WriteGate = new();
        private const long MaximumAuditFileBytes = 8L * 1024 * 1024;
        private const int RetainedAuditFileCount = 5;

        public static void Write(
            IAppPathProvider pathProvider,
            string action,
            ServerMigrationRequestContext requestContext,
            string packageId,
            bool? success,
            string message,
            DateTimeOffset timestampUtc)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            requestContext ??= new ServerMigrationRequestContext(string.Empty, string.Empty);
            string auditRoot = Path.Combine(pathProvider.LogRoot, "Security");
            string auditPath = Path.Combine(auditRoot, "server-migration.jsonl");
            var record = new
            {
                timestampUtc,
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
                RotateIfRequired(auditPath, Encoding.UTF8.GetByteCount(line));
                File.AppendAllText(auditPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                RuntimeFilePermissionHelper.RestrictFile(auditPath);
            }
        }

        private static void RotateIfRequired(string auditPath, int pendingBytes)
        {
            if (!File.Exists(auditPath))
            {
                return;
            }

            EnsureRegularAuditFile(auditPath);
            if (new FileInfo(auditPath).Length + pendingBytes <= MaximumAuditFileBytes)
            {
                return;
            }

            for (int index = RetainedAuditFileCount; index >= 1; index--)
            {
                string sourcePath = index == 1
                    ? auditPath
                    : GetArchivePath(auditPath, index - 1);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                EnsureRegularAuditFile(sourcePath);
                string destinationPath = GetArchivePath(auditPath, index);
                if (File.Exists(destinationPath))
                {
                    EnsureRegularAuditFile(destinationPath);
                }

                File.Move(sourcePath, destinationPath, overwrite: true);
                RuntimeFilePermissionHelper.RestrictFile(destinationPath);
            }
        }

        private static string GetArchivePath(string auditPath, int index) =>
            Path.Combine(
                Path.GetDirectoryName(auditPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(auditPath)}.{index}{Path.GetExtension(auditPath)}");

        private static void EnsureRegularAuditFile(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InfrastructureServiceException(
                    "服务器迁移安全审计文件不能是符号链接或重解析点。");
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

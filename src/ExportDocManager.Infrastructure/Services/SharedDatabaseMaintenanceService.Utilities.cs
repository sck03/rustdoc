using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService
    {
        private const long MaximumSettingsDiagnosticsBytes = 4L * 1024 * 1024;

        private void EnsurePostgreSqlReady()
        {
            if (!DatabaseModeHelper.UsesSharedDatabase(_databaseSettings))
            {
                throw new ServiceValidationException("当前未启用 PostgreSQL 团队版业务数据库，无法执行 PostgreSQL 物理备份。");
            }
        }

        private JsonNode ReadRedactedSettings()
        {
            string settingsPath = Path.Combine(_pathProvider.ConfigRoot, "appsettings.json");
            try
            {
                var settingsFile = new FileInfo(settingsPath);
                if (!settingsFile.Exists)
                {
                    return new JsonObject
                    {
                        ["exists"] = false
                    };
                }

                if (!IsRegularSupportPackageFile(settingsFile))
                {
                    return new JsonObject
                    {
                        ["exists"] = true,
                        ["readError"] = "配置文件是符号链接、重解析点或不可读取文件，已跳过。"
                    };
                }

                if (settingsFile.Length > MaximumSettingsDiagnosticsBytes)
                {
                    return new JsonObject
                    {
                        ["exists"] = true,
                        ["sizeBytes"] = settingsFile.Length,
                        ["readError"] = $"配置文件超过 {MaximumSettingsDiagnosticsBytes} 字节，已跳过。"
                    };
                }

                var node = JsonNode.Parse(File.ReadAllText(settingsPath, Encoding.UTF8)) ?? new JsonObject();
                RedactSensitiveProperties(node);
                return node;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new JsonObject
                {
                    ["exists"] = true,
                    ["readError"] = ex.Message
                };
            }
        }

        private static void RedactSensitiveProperties(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var property in obj.ToList())
                {
                    if (IsSensitiveKey(property.Key))
                    {
                        obj[property.Key] = "***";
                    }
                    else if (property.Value != null)
                    {
                        RedactSensitiveProperties(property.Value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        RedactSensitiveProperties(item);
                    }
                }
            }
        }

        private static bool IsSensitiveKey(string key)
        {
            var value = key ?? string.Empty;
            return value.Contains("password", StringComparison.OrdinalIgnoreCase)
                || value.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || value.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("token", StringComparison.OrdinalIgnoreCase)
                || value.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)
                || value.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || value.Contains("accesskey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("signingkey", StringComparison.OrdinalIgnoreCase)
                || value.Contains("encryptionkey", StringComparison.OrdinalIgnoreCase);
        }

        private static SharedDatabaseBackupItem ToBackupItem(FileInfo file)
        {
            return new SharedDatabaseBackupItem(
                file.Name,
                file.FullName,
                file.Exists ? file.Length : 0,
                file.CreationTime,
                file.LastWriteTime);
        }

        private string EnsureDirectory(string path)
        {
            try
            {
                string dataRoot = Path.GetFullPath(_pathProvider.DataRoot);
                string fullPath = PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    path,
                    dataRoot,
                    "受管维护目录不能包含符号链接或重解析点。");
                Directory.CreateDirectory(fullPath);
                return PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    fullPath,
                    dataRoot,
                    "受管维护目录不能包含符号链接或重解析点。");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InfrastructureServiceException("受管维护目录不可写，请检查运行目录权限。", ex);
            }
        }

        private static string NormalizeFileToken(string value)
        {
            var chars = (value ?? string.Empty)
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
                .ToArray();
            string token = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(token) ? "database" : token;
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private static string ToSqlLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private static string NormalizeSqlCommentValue(string value)
        {
            var output = new StringBuilder((value ?? string.Empty).Length);
            bool previousWasWhitespace = false;
            foreach (char ch in value ?? string.Empty)
            {
                if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                {
                    if (output.Length > 0 && !previousWasWhitespace)
                    {
                        output.Append(' ');
                    }
                    previousWasWhitespace = true;
                    continue;
                }

                output.Append(ch);
                previousWasWhitespace = false;
            }

            return output.ToString().Trim();
        }

        internal static string QuotePowerShellLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        internal static string QuotePosixShellArgument(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        }

        private static string ReadFileVersion(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                return FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsTextLog(string fileName)
        {
            return fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup for an interrupted package write.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup for a partially written restore plan.
            }
        }

        private sealed record PostgreSqlToolRunResult(
            string StandardOutput,
            string StandardError);

    }
}

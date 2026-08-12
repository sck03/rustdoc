using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.DataAccess
{
    internal static class PostgreSqlPasswordResolver
    {
        public const string PasswordEnvironmentVariable = "EXPORTDOCMANAGER_POSTGRES_PASSWORD";
        public const string PasswordFileEnvironmentVariable = "EXPORTDOCMANAGER_POSTGRES_PASSWORD_FILE";
        private const int MaximumSecretFileBytes = 16 * 1024;

        public static string Resolve(string configuredValue, IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string? environmentSecret = ResolveEnvironmentSecret(
                PasswordEnvironmentVariable,
                PasswordFileEnvironmentVariable,
                pathProvider);
            if (environmentSecret != null)
            {
                return environmentSecret;
            }

            if (string.IsNullOrEmpty(configuredValue))
            {
                return string.Empty;
            }

            string? decrypted = SecurityHelper.Decrypt(configuredValue);
            if (decrypted == null)
            {
                throw new ServiceValidationException(
                    $"PostgreSQL 密码不能以明文保存在 appsettings.json。请使用 {PasswordEnvironmentVariable}、" +
                    $"{PasswordFileEnvironmentVariable}，或由程序保存 AES-GCM 受保护载荷。");
            }

            return decrypted;
        }

        internal static string? ResolveEnvironmentSecret(
            string passwordEnvironmentVariable,
            string passwordFileEnvironmentVariable,
            IAppPathProvider pathProvider)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordEnvironmentVariable);
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordFileEnvironmentVariable);
            ArgumentNullException.ThrowIfNull(pathProvider);

            string? passwordFile = Environment.GetEnvironmentVariable(passwordFileEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(passwordFile))
            {
                return ReadPasswordFile(
                    passwordFile.Trim(),
                    passwordFileEnvironmentVariable,
                    pathProvider);
            }

            return Environment.GetEnvironmentVariable(passwordEnvironmentVariable);
        }

        private static string ReadPasswordFile(
            string configuredPath,
            string passwordFileEnvironmentVariable,
            IAppPathProvider pathProvider)
        {
            bool relative = !Path.IsPathRooted(configuredPath);
            string path = relative
                ? Path.GetFullPath(Path.Combine(pathProvider.SecurityRoot, configuredPath))
                : Path.GetFullPath(configuredPath);

            if (relative && !PathBoundaryHelper.IsWithinRoot(path, pathProvider.SecurityRoot))
            {
                throw new ServiceValidationException(
                    $"{passwordFileEnvironmentVariable} 的相对路径必须位于运行数据 Security 目录内。");
            }
            if (!File.Exists(path))
            {
                throw new ResourceNotFoundException($"PostgreSQL 密码文件不存在：{path}");
            }

            var info = new FileInfo(path);
            if (info.Length > MaximumSecretFileBytes)
            {
                throw new ServiceValidationException("PostgreSQL 密码文件超过 16 KiB 安全上限。");
            }
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ServiceValidationException("PostgreSQL 密码文件不能是符号链接或重解析点。");
            }

            try
            {
                return File.ReadAllText(path).TrimEnd('\r', '\n');
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InfrastructureServiceException($"PostgreSQL 密码文件无法读取：{path}", ex);
            }
        }
    }
}

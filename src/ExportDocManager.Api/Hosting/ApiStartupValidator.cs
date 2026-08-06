using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiStartupValidator
    {
        public static void Validate(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            ArgumentNullException.ThrowIfNull(runtimeOptions);

            ValidateListenUrls(runtimeOptions, databaseSettings);
            ValidateBootstrapToken(runtimeOptions, databaseSettings);
            ValidateRuntimeDirectories(pathProvider);
            ValidateDatabasePath(pathProvider, databaseSettings);
        }

        public static void ValidateLocalListenUrls(string listenUrls)
        {
            foreach (string rawUrl in (listenUrls ?? string.Empty).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                {
                    throw new InvalidOperationException($"API 监听地址无效: {rawUrl}");
                }
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException($"API 监听地址只支持 HTTP/HTTPS: {rawUrl}");
                }

                if (!IsLoopbackHost(uri.Host))
                {
                    throw new InvalidOperationException(
                        $"API sidecar 只允许监听本机回环地址，当前地址为: {rawUrl}");
                }
            }
        }

        public static void ValidateListenUrls(
            ApiRuntimeOptions runtimeOptions,
            DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            ArgumentNullException.ThrowIfNull(databaseSettings);

            bool hasNetworkListener = false;
            foreach (string rawUrl in (runtimeOptions.ListenUrls ?? string.Empty).Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                {
                    throw new InvalidOperationException($"API 监听地址无效: {rawUrl}");
                }
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException($"API 监听地址只支持 HTTP/HTTPS: {rawUrl}");
                }

                hasNetworkListener |= !IsLoopbackHost(uri.Host);
            }

            if (!hasNetworkListener)
            {
                ValidateLocalListenUrls(runtimeOptions.ListenUrls);
                return;
            }

            if (!runtimeOptions.NetworkMode)
            {
                throw new InvalidOperationException("非回环监听必须显式启用 network mode。");
            }

            if (!DatabaseModeHelper.UsesPostgreSql(databaseSettings) ||
                !DatabaseModeHelper.HasCompletePostgreSqlConfiguration(databaseSettings))
            {
                throw new InvalidOperationException("局域网/容器 network mode 必须使用已完整配置的 PostgreSQL 数据库。");
            }

        }

        public static void ValidateBootstrapToken(
            ApiRuntimeOptions runtimeOptions,
            DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            if (!runtimeOptions.NetworkMode || !DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(runtimeOptions.BootstrapToken) ||
                runtimeOptions.BootstrapToken.Length < 24)
            {
                throw new InvalidOperationException(
                    $"network mode 必须配置至少 24 个字符的 {ApiRuntimeOptions.BootstrapTokenEnvironmentVariable}，用于保护首次管理员初始化。");
            }
        }

        private static void ValidateRuntimeDirectories(IAppPathProvider pathProvider)
        {
            if (!Directory.Exists(pathProvider.AppRoot))
            {
                throw new InvalidOperationException($"程序运行目录不存在: {pathProvider.AppRoot}");
            }

            EnsureNotReparsePoint(pathProvider.DataRoot, "业务数据目录");
            EnsureWritableDirectory(pathProvider.DataRoot, "业务数据目录");
            EnsureWritableDirectory(pathProvider.DatabaseRoot, "数据库目录");
            EnsureWritableDirectory(pathProvider.SingleWindowRoot, "单一窗口数据目录");
            EnsureWritableDirectory(pathProvider.CacheRoot, "缓存目录");
            EnsureWritableDirectory(pathProvider.ConfigRoot, "配置目录");
        }

        private static void ValidateDatabasePath(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings)
        {
            if (DatabaseModeHelper.UsesPostgreSql(databaseSettings))
            {
                string validationMessage = DatabaseModeHelper.Validate(databaseSettings);
                if (!string.IsNullOrWhiteSpace(validationMessage))
                {
                    throw new InvalidOperationException(validationMessage);
                }

                return;
            }

            string databasePath;
            try
            {
                databasePath = DbHelper.ResolveRuntimeSqliteDatabasePath(
                    pathProvider,
                    databaseSettings.SqliteDatabaseFileName);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }

            EnsureWritableDirectory(Path.GetDirectoryName(databasePath), "SQLite 数据库目录");
        }

        private static void EnsureWritableDirectory(string directory, string description)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"{description}不能为空。");
            }

            Directory.CreateDirectory(directory);
            EnsureNotReparsePoint(directory, description);

            string probePath = Path.Combine(directory, $".write-check-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(probePath, string.Empty);
            }
            finally
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
        }

        private static void EnsureNotReparsePoint(string directory, string description)
        {
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"{description}不能是符号链接或 Windows 重解析点: {directory}");
                }
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException($"{description}不存在: {directory}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new InvalidOperationException($"{description}不存在: {directory}", ex);
            }
        }

        private static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
        }
    }
}

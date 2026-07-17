using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiStartupValidator
    {
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static void Validate(
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings,
            ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            ArgumentNullException.ThrowIfNull(runtimeOptions);

            ValidateListenUrls(runtimeOptions, databaseSettings);
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

        private static void ValidateRuntimeDirectories(IAppPathProvider pathProvider)
        {
            if (!Directory.Exists(pathProvider.AppRoot))
            {
                throw new InvalidOperationException($"程序运行目录不存在: {pathProvider.AppRoot}");
            }

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

            string sqliteFileName = DbHelper.NormalizeSqliteDatabaseFileName(databaseSettings.SqliteDatabaseFileName);
            string databasePath = DbHelper.GetDatabasePath(sqliteFileName);
            EnsureWritableDirectory(Path.GetDirectoryName(databasePath), "SQLite 数据库目录");

            if (!Path.IsPathRooted(sqliteFileName) &&
                !IsPathUnderRoot(databasePath, pathProvider.DatabaseRoot))
            {
                throw new InvalidOperationException(
                    $"相对 SQLite 数据库路径必须解析到运行目录数据库目录下: {databasePath}");
            }
        }

        private static void EnsureWritableDirectory(string directory, string description)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"{description}不能为空。");
            }

            Directory.CreateDirectory(directory);

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

        private static bool IsPathUnderRoot(string path, string root)
        {
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedRoot, PathComparison) ||
                normalizedPath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    PathComparison);
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

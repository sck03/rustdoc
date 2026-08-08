using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

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
            PrepareRuntimeDirectories(pathProvider);
            ValidateEndpointPublication(pathProvider, runtimeOptions);
            ValidateDatabasePath(pathProvider, databaseSettings);
        }

        public static void ValidateEndpointPublication(
            IAppPathProvider pathProvider,
            ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            if (string.IsNullOrWhiteSpace(runtimeOptions.EndpointFile))
            {
                return;
            }

            if (runtimeOptions.NetworkMode || string.IsNullOrWhiteSpace(runtimeOptions.DesktopAccessToken))
            {
                throw new InvalidOperationException("动态端点文件只允许由带桌面令牌的本机 sidecar 使用。");
            }

            string[] listenUrls = (runtimeOptions.ListenUrls ?? string.Empty).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (listenUrls.Length != 1 ||
                !Uri.TryCreate(listenUrls[0], UriKind.Absolute, out Uri uri) ||
                uri.Scheme != Uri.UriSchemeHttp ||
                !IsLoopbackHost(uri.Host) ||
                uri.Port != 0)
            {
                throw new InvalidOperationException(
                    "动态端点文件要求 API 仅监听 http://127.0.0.1:0 或等价 IPv6 回环地址。");
            }

            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                runtimeOptions.EndpointFile,
                pathProvider.CacheRoot,
                "动态端点文件必须位于运行数据根 Cache 目录内。");
        }

        public static void PrepareRuntimeDirectories(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ValidateRuntimeDirectories(pathProvider);
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

            string dataRoot = pathProvider.DataRoot;
            if (Path.GetPathRoot(dataRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    PathBoundaryHelper.PathComparison) == true)
            {
                throw new InvalidOperationException("业务数据目录不能是文件系统根目录。");
            }

            try
            {
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    dataRoot,
                    "业务数据目录不能包含符号链接或 Windows 重解析点。");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

            var managedDirectories = new (string Path, string Description)[]
            {
                (dataRoot, "业务数据目录"),
                (pathProvider.DatabaseRoot, "数据库目录"),
                (pathProvider.UserTemplateRoot, "用户模板目录"),
                (pathProvider.FileRoot, "业务文件目录"),
                (pathProvider.ExportRoot, "导出目录"),
                (Path.Combine(pathProvider.ExportRoot, "Browser"), "浏览器下载目录"),
                (pathProvider.BackupRoot, "备份目录"),
                (Path.Combine(pathProvider.BackupRoot, "PostgreSQL"), "PostgreSQL 备份目录"),
                (pathProvider.SingleWindowRoot, "单一窗口数据目录"),
                (pathProvider.LogRoot, "日志目录"),
                (pathProvider.CacheRoot, "缓存目录"),
                (Path.Combine(pathProvider.CacheRoot, "BackgroundJobs"), "后台任务缓存目录"),
                (pathProvider.ConfigRoot, "配置目录"),
                (pathProvider.SecurityRoot, "安全数据目录"),
                (pathProvider.WebViewRoot, "WebView 数据目录"),
                (Path.Combine(dataRoot, "Marks"), "唛头图片目录"),
                (Path.Combine(dataRoot, "SupportPackages"), "技术支持包目录")
            };

            foreach ((string directory, string description) in managedDirectories)
            {
                EnsureWritableDirectory(directory, description, dataRoot);
            }
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
            catch (ServiceException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new ServiceValidationException(exception.Message, exception);
            }

            EnsureWritableDirectory(
                Path.GetDirectoryName(databasePath),
                "SQLite 数据库目录",
                pathProvider.DataRoot);
        }

        private static void EnsureWritableDirectory(
            string directory,
            string description,
            string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"{description}不能为空。");
            }

            try
            {
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    directory,
                    dataRoot,
                    $"{description}不能包含符号链接或 Windows 重解析点。");
                Directory.CreateDirectory(directory);
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    directory,
                    dataRoot,
                    $"{description}不能包含符号链接或 Windows 重解析点。");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

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

        internal static bool IsLoopbackHost(string host)
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

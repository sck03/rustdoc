using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed record ApiRuntimePathInfo(
        string Key,
        string Label,
        string Path,
        string StorageClass,
        string AccessMode,
        string Requirement,
        bool Exists,
        string Description);

    public static class ApiRuntimePathRequirement
    {
        public const string Core = "core";
        public const string Feature = "feature";
        public const string Optional = "optional";
    }

    public sealed record ApiRuntimeDependencyInfo(
        string Key,
        string Label,
        string Requirement,
        string Status,
        bool Ready,
        string ResolvedPath,
        string Message);

    public sealed record ApiHealthResponse(
        string Status,
        DateTimeOffset CheckedAt,
        string ProductVersion,
        string InformationalVersion,
        string AppRoot,
        string DataRoot,
        string DatabaseRoot,
        string SingleWindowRoot,
        string TemplateRoot,
        string OcrModelRoot,
        string LogRoot,
        string DatabaseProvider,
        string SqliteDatabasePath,
        IReadOnlyList<ApiRuntimePathInfo> RuntimePaths,
        IReadOnlyList<ApiRuntimeDependencyInfo> RuntimeDependencies,
        string StoragePolicy);

    public static class ApiHealthResponseFactory
    {
        public static ApiHealthResponse CreatePublic(DatabaseConnectionSettings databaseSettings)
        {
            ArgumentNullException.ThrowIfNull(databaseSettings);

            return new ApiHealthResponse(
                "ok",
                DateTimeOffset.UtcNow,
                ProductVersionProvider.ProductVersion,
                ProductVersionProvider.InformationalVersion,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                DatabaseModeHelper.GetCurrentModeText(databaseSettings),
                string.Empty,
                Array.Empty<ApiRuntimePathInfo>(),
                Array.Empty<ApiRuntimeDependencyInfo>(),
                "公开健康检查只返回服务版本、状态和数据库模式；服务器绝对路径与依赖明细仅向桌面可信连接或管理员账号返回。");
        }

        public static ApiHealthResponse Create(
            IAppPathProvider paths,
            DatabaseConnectionSettings databaseSettings,
            string sqliteDatabasePath,
            IReadOnlyList<RuntimeDependencyDiagnostic> runtimeDependencies)
        {
            ArgumentNullException.ThrowIfNull(paths);
            ArgumentNullException.ThrowIfNull(databaseSettings);
            ArgumentNullException.ThrowIfNull(runtimeDependencies);

            var runtimePaths = CreateRuntimePaths(paths, sqliteDatabasePath);

            return new ApiHealthResponse(
                "ok",
                DateTimeOffset.UtcNow,
                ProductVersionProvider.ProductVersion,
                ProductVersionProvider.InformationalVersion,
                paths.AppRoot,
                paths.DataRoot,
                paths.DatabaseRoot,
                paths.SingleWindowRoot,
                paths.TemplateRoot,
                paths.OcrModelRoot,
                paths.LogRoot,
                DatabaseModeHelper.GetCurrentModeText(databaseSettings),
                sqliteDatabasePath ?? string.Empty,
                runtimePaths,
                runtimeDependencies
                    .Select(ToApiRuntimeDependency)
                    .ToArray(),
                "程序根只保存可执行文件以及 Templates/Resources/Browsers/Tools/OcrModels 等只读随程序资源。设置、用户模板、数据库、日志、缓存、备份、WebView 和其它业务可写数据统一使用 data root（默认 App_Data，可由 --data-root 指向其它磁盘）；授权镜像保存到运行数据根 Security，试用锚点和已注册许可证保存到平台机器级授权锚点。");
        }

        private static ApiRuntimeDependencyInfo ToApiRuntimeDependency(RuntimeDependencyDiagnostic diagnostic)
        {
            return new ApiRuntimeDependencyInfo(
                diagnostic.Key,
                diagnostic.Label,
                diagnostic.Requirement,
                diagnostic.Status,
                diagnostic.Ready,
                diagnostic.ResolvedPath,
                diagnostic.Message);
        }

        private static IReadOnlyList<ApiRuntimePathInfo> CreateRuntimePaths(
            IAppPathProvider paths,
            string sqliteDatabasePath)
        {
            var result = new List<ApiRuntimePathInfo>
            {
                DirectoryPath("app-root", "程序根", paths.AppRoot, "program-resource", "read-only", ApiRuntimePathRequirement.Core, "程序可执行文件和随程序资源的只读根目录。"),
                DirectoryPath("template-root", "内置报表模板", paths.TemplateRoot, "program-resource", "read-only", ApiRuntimePathRequirement.Feature, "随程序发布，只读加载且不在运行时改写。"),
                DirectoryPath("resource-root", "公共资源", paths.ResourceRoot, "program-resource", "read-only", ApiRuntimePathRequirement.Feature, "Excel 模板、单一窗口词典等随程序资源。"),
                DirectoryPath("browser-root", "浏览器运行包", paths.BrowserRoot, "program-resource", "read-only", ApiRuntimePathRequirement.Feature, "报表渲染使用的跨平台浏览器资源。"),
                DirectoryPath("tool-root", "工具运行包", paths.ToolRoot, "program-resource", "read-only", ApiRuntimePathRequirement.Optional, "PostgreSQL、Excel helper 等可选工具。"),
                DirectoryPath("ocr-model-root", "OCR 模型", paths.OcrModelRoot, "program-resource", "read-only", ApiRuntimePathRequirement.Optional, "可选 OCR 模型与原生运行资源。"),
                DirectoryPath("data-root", "运行数据根", paths.DataRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "全部默认业务可写目录的统一根。"),
                DirectoryPath("user-template-root", "用户报表模板", paths.UserTemplateRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Feature, "用户新建、编辑副本和导入的报表模板。"),
                DirectoryPath("database-root", "数据库目录", paths.DatabaseRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "SQLite 数据库及数据库相关运行文件。"),
                DirectoryPath("file-root", "业务文件", paths.FileRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "业务附件和受管文件。"),
                DirectoryPath("export-root", "导出目录", paths.ExportRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "未显式选择外部目录时的受管导出位置。"),
                DirectoryPath("backup-root", "备份目录", paths.BackupRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "SQLite、PostgreSQL 与云备份工作目录。"),
                DirectoryPath("single-window-root", "单一窗口目录", paths.SingleWindowRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "交接包、回执和客户端交换数据。"),
                DirectoryPath("log-root", "日志目录", paths.LogRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "API、OCR、前端和桌面正常运行日志。"),
                DirectoryPath("cache-root", "缓存目录", paths.CacheRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "可清理的短生命周期任务缓存。"),
                DirectoryPath("config-root", "运行配置", paths.ConfigRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "运行路径与本机配置数据。"),
                DirectoryPath("security-root", "安全数据", paths.SecurityRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "授权镜像等本机安全数据。"),
                DirectoryPath("webview-root", "WebView 数据", paths.WebViewRoot, "runtime-data", "read-write", ApiRuntimePathRequirement.Core, "桌面 WebView 配置、缓存和浏览器存储。")
            };

            if (!string.IsNullOrWhiteSpace(sqliteDatabasePath))
            {
                result.Add(new ApiRuntimePathInfo(
                    "sqlite-database",
                    "SQLite 文件",
                    Path.GetFullPath(sqliteDatabasePath),
                    "database-file",
                    "read-write",
                    ApiRuntimePathRequirement.Core,
                    File.Exists(sqliteDatabasePath),
                    "当前 SQLite 单机数据库文件。"));
            }

            return result;
        }

        private static ApiRuntimePathInfo DirectoryPath(
            string key,
            string label,
            string path,
            string storageClass,
            string accessMode,
            string requirement,
            string description)
        {
            return new ApiRuntimePathInfo(
                key,
                label,
                Path.GetFullPath(path),
                storageClass,
                accessMode,
                requirement,
                Directory.Exists(path),
                description);
        }
    }
}

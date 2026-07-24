using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string ShutdownMaintenanceStoragePolicy =
            "Tauri 退出维护只允许由桌面 token 保护的本机 sidecar 触发；按系统设置清理运行数据根 Backups、运行数据根数据库 AuditLogs 表和运行数据根 Logs，并只把当前数据库最新 ZIP 备份上传到已保存的 WebDAV。该流程不读取发票/报关业务表，不读取付款/报销业务表，也不生成系统用户目录或全局程序数据目录默认落点。";

        private const string SystemLogCleanupStoragePolicy =
            "手动日志清理只读取已保存的系统日志保留设置；审计日志清理仅操作运行数据根数据库 AuditLogs 表，文本日志清理仅操作运行数据根 Logs 下的文本日志文件。该流程不读取发票/报关业务表，不读取付款/报销业务表，不接收任意路径，也不生成系统用户目录或全局程序数据目录默认落点。";

        private static void MapSystemEndpoints(
            this IEndpointRouteBuilder endpoints,
            ApiRuntimeOptions runtimeOptions,
            DatabaseConnectionSettings databaseSettings)
        {
            endpoints.MapGet("/", () => Results.Redirect("/swagger"));

            endpoints.MapGet("/readyz", () => Results.Ok(new { status = "ok" }))
                .WithName("Readyz");

            endpoints.MapGet("/healthz", async (
                HttpContext context,
                IAppPathProvider paths,
                IRuntimeDependencyDiagnosticsService dependencyDiagnostics,
                ApiCurrentUserResolver currentUserResolver,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                string sqliteDatabasePath = DatabaseModeHelper.UsesPostgreSql(databaseSettings)
                    ? string.Empty
                    : DbHelper.GetDatabasePath(databaseSettings.SqliteDatabaseFileName);

                var response = ApiHealthResponseFactory.Create(
                    paths,
                    databaseSettings,
                    sqliteDatabasePath,
                    dependencyDiagnostics.Inspect());
                var user = await currentUserResolver.ResolveAsync(context, context.RequestAborted);
                bool canViewDetails = ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions) ||
                    authorizationService.CanManageSettings(user);
                return Results.Ok(canViewDetails ? response : ApiHealthResponseFactory.CreatePublic(response));
            })
            .WithName("Healthz");

            endpoints.MapGet("/openapi/v1.json", async (
                HttpContext context,
                ApiCurrentUserResolver currentUserResolver,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                var accessError = await GetApiDocumentationAccessErrorAsync(
                    context,
                    runtimeOptions,
                    currentUserResolver,
                    authorizationService,
                    desktopAccessOptions);
                if (accessError != null)
                {
                    return accessError;
                }

                return Results.Json(OpenApiDocumentFactory.Create(runtimeOptions));
            })
                .WithName("OpenApiJson");

            endpoints.MapGet("/swagger/v1/swagger.json", async (
                HttpContext context,
                ApiCurrentUserResolver currentUserResolver,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                var accessError = await GetApiDocumentationAccessErrorAsync(
                    context,
                    runtimeOptions,
                    currentUserResolver,
                    authorizationService,
                    desktopAccessOptions);
                if (accessError != null)
                {
                    return accessError;
                }

                return Results.Json(OpenApiDocumentFactory.Create(runtimeOptions));
            })
                .WithName("SwaggerJson");

            endpoints.MapGet("/swagger", async (
                HttpContext context,
                ApiCurrentUserResolver currentUserResolver,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                var accessError = await GetApiDocumentationAccessErrorAsync(
                    context,
                    runtimeOptions,
                    currentUserResolver,
                    authorizationService,
                    desktopAccessOptions);
                if (accessError != null)
                {
                    return accessError;
                }

                return Results.Content(
                    OpenApiDocumentFactory.CreateSwaggerLandingPage(),
                    "text/html; charset=utf-8");
            })
                .WithName("Swagger");

            endpoints.MapPost("/api/system/shutdown-maintenance", async (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                IShutdownMaintenanceService shutdownMaintenanceService,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("退出维护只能由 Tauri 桌面端通过 desktop token 触发。");
                }

                try
                {
                    var result = await shutdownMaintenanceService.RunAsync(cancellationToken).ConfigureAwait(false);
                    return Results.Ok(CreateShutdownMaintenanceResponse(
                        success: true,
                        message: "退出维护已完成。",
                        result,
                        pathProvider));
                }
                catch (OperationCanceledException)
                {
                    return Results.Ok(CreateShutdownMaintenanceResponse(
                        success: false,
                        message: "退出维护已取消，桌面程序将继续关闭。",
                        new ShutdownMaintenanceResult(),
                        pathProvider));
                }
                catch (Exception ex)
                {
                    return Results.Ok(CreateShutdownMaintenanceResponse(
                        success: false,
                        message: $"退出维护未完全完成：{ex.Message}",
                        new ShutdownMaintenanceResult(),
                        pathProvider));
                }
            })
            .WithName("RunShutdownMaintenance");

            endpoints.MapPost("/api/system/logs/cleanup", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISystemLogCleanupService logCleanupService,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以清理系统日志。");
                }

                try
                {
                    var result = await logCleanupService.CleanAsync(cancellationToken).ConfigureAwait(false);
                    return Results.Ok(CreateSystemLogCleanupResponse(
                        true,
                        $"日志清理已完成：审计日志 {result.DeletedAuditLogs} 条，文本日志 {result.DeletedTextLogs} 个。",
                        result,
                        pathProvider));
                }
                catch (OperationCanceledException)
                {
                    return Results.Ok(CreateSystemLogCleanupResponse(
                        false,
                        "日志清理已取消。",
                        new SystemLogCleanupResult(),
                        pathProvider));
                }
                catch (Exception ex)
                {
                    return WriteConflict($"日志清理失败：{ex.Message}");
                }
            })
            .WithName("CleanupSystemLogs");
        }

        private static async Task<IResult> GetApiDocumentationAccessErrorAsync(
            HttpContext context,
            ApiRuntimeOptions runtimeOptions,
            ApiCurrentUserResolver currentUserResolver,
            ApiAuthorizationService authorizationService,
            ApiDesktopAccessOptions desktopAccessOptions)
        {
            if (!ApiEndpointAuth.RequiresDocumentationAuthentication(runtimeOptions) ||
                ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
            {
                return null;
            }

            var user = await currentUserResolver.ResolveAsync(context, context.RequestAborted)
                .ConfigureAwait(false);
            if (user == null)
            {
                return Results.Json(
                    new ApiErrorResponse("网络部署下，API 文档仅向已登录管理员开放。"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return authorizationService.CanManageSettings(user)
                ? null
                : WriteForbidden("只有管理员可以查看 API 文档。");
        }

        private static ApiShutdownMaintenanceResponse CreateShutdownMaintenanceResponse(
            bool success,
            string message,
            ShutdownMaintenanceResult result,
            IAppPathProvider pathProvider)
        {
            result ??= new ShutdownMaintenanceResult();
            return new ApiShutdownMaintenanceResponse(
                success,
                message ?? string.Empty,
                result.DeletedAuditLogs,
                result.DeletedTextLogs,
                result.UploadedBackupFileName ?? string.Empty,
                result.CloudSyncErrorMessage ?? string.Empty,
                result.CloudSyncFailed,
                pathProvider.BackupRoot,
                pathProvider.LogRoot,
                ShutdownMaintenanceStoragePolicy);
        }

        private static ApiSystemLogCleanupResponse CreateSystemLogCleanupResponse(
            bool success,
            string message,
            SystemLogCleanupResult result,
            IAppPathProvider pathProvider)
        {
            result ??= new SystemLogCleanupResult();
            return new ApiSystemLogCleanupResponse(
                success,
                message ?? string.Empty,
                result.DeletedAuditLogs,
                result.DeletedTextLogs,
                result.DeletedTextLogsByAge,
                result.DeletedTextLogsByCount,
                pathProvider.LogRoot,
                SystemLogCleanupStoragePolicy);
        }
    }
}

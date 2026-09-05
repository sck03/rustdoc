using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string BackupRestoreConfirmationText = "RESTORE";
        private const string BackupStoragePolicy =
            "数据库备份默认写入运行数据根 Backups/，只枚举和还原当前数据库对应的备份包；不读取发票/付款业务表，不生成导出目录，不写系统用户配置目录、全局程序数据目录或系统 C 盘默认落点。";
        private const string CloudBackupStoragePolicy =
            "WebDAV 云备份只读取运行数据根 Config/appsettings.json 中已保存的 WebDAV 配置，并只上传运行数据根 Backups/ 中当前数据库对应的最新 ZIP 备份；不接受任意本地路径，不读取发票/报关业务表，也不读取付款/报销业务表。";

        private static void MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/backup", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                IAppPathProvider pathProvider,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看和管理数据库备份。");
                }

                return Results.Ok(CreateBackupListResponse(
                    backupService,
                    pathProvider,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)));
            })
            .WithName("ListDatabaseBackups")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<ApiBackupListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/backup", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiBackgroundJobRunner jobRunner) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以创建数据库备份。");
                }

                return AcceptedBackgroundJob(EnqueueDatabaseBackupJob(jobRunner, user.Username));
            })
            .WithName("CreateDatabaseBackup")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);

            endpoints.MapPost("/api/backup/cleanup", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                IAppPathProvider pathProvider,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackupCleanupRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以清理数据库备份。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("备份清理请求体不能为空。"));
                }

                if (request.DaysToKeep < 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("保留天数不能小于 0。"));
                }

                backupService.CleanOldBackups(request.DaysToKeep);
                var list = CreateBackupListResponse(
                    backupService,
                    pathProvider,
                    ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions));
                return Results.Ok(new ApiBackupCreateResponse(
                    true,
                    request.DaysToKeep == 0 ? "保留天数为 0，未清理备份。" : "旧备份清理完成。",
                    list.Backups,
                    list.BackupRoot,
                    list.StoragePolicy));
            })
            .WithName("CleanupDatabaseBackups")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<ApiBackupCreateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/backup/restore", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                ApiBackgroundJobRunner jobRunner,
                ApiBackupRestoreRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以还原数据库备份。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("备份还原请求体不能为空。"));
                }

                if (!string.Equals(request.ConfirmationText?.Trim(), BackupRestoreConfirmationText, StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse($"还原数据库前需要输入确认文本 {BackupRestoreConfirmationText}。"));
                }

                if (!TryResolveKnownBackupPath(backupService, request.BackupFileName, out var backupPath, out var errorMessage))
                {
                    return Results.BadRequest(new ApiErrorResponse(errorMessage));
                }

                return AcceptedBackgroundJob(EnqueueDatabaseRestoreJob(
                    jobRunner,
                    user.Username,
                    backupPath));
            })
            .WithName("RestoreDatabaseBackup")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);

            endpoints.MapGet("/api/backup/disaster-recovery/status", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISingleWindowDisasterRecoveryService recoveryService,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看持卡机灾难恢复状态。");
                }

                var status = recoveryService.GetStatus();
                return Results.Ok(new ApiDisasterRecoveryStatusResponse(
                    status.Supported,
                    status.UsesSqlite,
                    status.PendingRestore,
                    ApiResponsePathPolicy.Reveal(
                        status.RecoveryRoot,
                        ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)),
                    status.Message,
                    status.StoragePolicy));
            })
            .WithName("GetDisasterRecoveryStatus")
            .WithApiCapability(PermissionResourceCatalog.SystemDisasterRecovery, PermissionAction.Manage)
            .Produces<ApiDisasterRecoveryStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/backup/disaster-recovery/create", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiDisasterRecoveryCreateRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以创建持卡机灾难恢复包。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("持卡机灾难恢复包只能由受信任的桌面版创建。");
                }

                if (request == null || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new ApiErrorResponse("恢复包密码不能为空。"));
                }

                return AcceptedBackgroundJob(EnqueueDisasterRecoveryPackageJob(
                    jobRunner,
                    user.Username,
                    request.Password));
            })
            .WithName("CreateDisasterRecoveryPackage")
            .WithApiCapability(PermissionResourceCatalog.SystemDisasterRecovery, PermissionAction.Manage)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);

            endpoints.MapPost("/api/backup/disaster-recovery/restore", (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiDisasterRecoveryRestoreRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以安排持卡机灾难恢复。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("持卡机灾难恢复只能由受信任的桌面版执行。");
                }

                if (request == null ||
                    string.IsNullOrWhiteSpace(request.PackagePath) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new ApiErrorResponse("恢复包路径和密码不能为空。"));
                }

                if (!string.Equals(request.ConfirmationText?.Trim(), "RECOVER", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse("安排灾难恢复前需要输入确认文本 RECOVER。"));
                }

                return AcceptedBackgroundJob(EnqueueDisasterRecoveryRestoreJob(
                    jobRunner,
                    user.Username,
                    request.PackagePath,
                    request.Password));
            })
            .WithName("RestoreDisasterRecoveryPackage")
            .WithApiCapability(PermissionResourceCatalog.SystemDisasterRecovery, PermissionAction.Manage)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);

            endpoints.MapGet("/api/backup/cloud/status", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                IBackupService backupService,
                IAppPathProvider pathProvider,
                ApiDesktopAccessOptions desktopAccessOptions) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看 WebDAV 云备份状态。");
                }

                await settingsService.LoadAsync(context.RequestAborted);
                var webDav = settingsService.Settings.WebDav;
                var latestBackup = GetLatestBackupFile(backupService);
                return Results.Ok(new ApiCloudBackupStatusResponse(
                    webDav.Enabled,
                    IsWebDavConfigured(webDav),
                    webDav.Url?.Trim() ?? string.Empty,
                    webDav.UserName?.Trim() ?? string.Empty,
                    latestBackup?.Name ?? string.Empty,
                    latestBackup?.Length ?? 0,
                    ApiResponsePathPolicy.Reveal(
                        pathProvider.BackupRoot,
                        ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)),
                    CloudBackupStoragePolicy));
            })
            .WithName("GetCloudBackupStatus")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<ApiCloudBackupStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/backup/cloud/test-connection", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ICloudSyncService cloudSyncService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以测试 WebDAV 云备份连接。");
                }

                await settingsService.LoadAsync(cancellationToken);
                var webDav = settingsService.Settings.WebDav;
                if (!IsWebDavConfigured(webDav))
                {
                    return WriteValidation("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                bool success = await cloudSyncService.TestConnectionAsync(webDav, cancellationToken);
                if (!success)
                {
                    return WriteInfrastructureFailure(
                        "WebDAV 连接服务暂时不可用，请检查地址、账号、密码或目录权限后重试。",
                        new HttpRequestException("WebDAV connection test failed."));
                }

                return Results.Ok(new ApiCloudBackupCommandResponse(
                    true,
                    "WebDAV 连接测试成功。",
                    string.Empty,
                    string.Empty,
                    0,
                    string.Empty,
                    CloudBackupStoragePolicy));
            })
            .WithName("TestCloudBackupConnection")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<ApiCloudBackupCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/backup/cloud/upload-latest", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                IBackupService backupService,
                ApiBackgroundJobRunner jobRunner) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以上传数据库备份到 WebDAV。");
                }

                await settingsService.LoadAsync(context.RequestAborted);
                var webDav = settingsService.Settings.WebDav;
                if (!webDav.Enabled)
                {
                    return WriteValidation("WebDAV 云备份未启用，请先保存启用状态。");
                }

                if (!IsWebDavConfigured(webDav))
                {
                    return WriteValidation("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                var latestBackup = GetLatestBackupFile(backupService);
                if (latestBackup == null)
                {
                    return WriteNotFound("当前没有可上传的数据库备份，请先创建本地备份。");
                }

                return AcceptedBackgroundJob(EnqueueCloudBackupUploadJob(jobRunner, user.Username));
            })
            .WithName("UploadLatestDatabaseBackupToCloud")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);

            endpoints.MapGet("/api/backup/cloud/backups", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ICloudSyncService cloudSyncService,
                IBackupService backupService,
                IAppPathProvider pathProvider,
                ApiDesktopAccessOptions desktopAccessOptions,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看 WebDAV 云备份列表。");
                }

                await settingsService.LoadAsync(cancellationToken);
                var webDav = settingsService.Settings.WebDav;
                if (!webDav.Enabled)
                {
                    return WriteValidation("WebDAV 云备份未启用，请先保存启用状态。");
                }

                if (!IsWebDavConfigured(webDav))
                {
                    return WriteValidation("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                try
                {
                    var remoteBackups = await cloudSyncService.ListBackupFilesAsync(cancellationToken);
                    return Results.Ok(new ApiCloudBackupListResponse(
                        remoteBackups.Select(ToCloudBackupItemDto).ToArray(),
                        ApiResponsePathPolicy.Reveal(
                            pathProvider.BackupRoot,
                            ApiResponsePathPolicy.CanReveal(context, desktopAccessOptions)),
                        CloudBackupStoragePolicy));
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is ArgumentException ||
                    ex is IOException ||
                    ex is HttpRequestException ||
                    ex is System.Xml.XmlException)
                {
                    return WriteInfrastructureFailure("WebDAV 云备份列表服务暂时不可用，请稍后重试。", ex);
                }
            })
            .WithName("ListCloudDatabaseBackups")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<ApiCloudBackupListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/backup/cloud/download", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ApiCloudBackupDownloadRequest request,
                ApiBackgroundJobRunner jobRunner) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以下载 WebDAV 云备份。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("云备份下载请求体不能为空。"));
                }

                if (!TryNormalizeCloudBackupFileName(request.RemoteFileName, out var remoteFileName, out var fileNameError))
                {
                    return Results.BadRequest(new ApiErrorResponse(fileNameError));
                }

                await settingsService.LoadAsync(context.RequestAborted);
                var webDav = settingsService.Settings.WebDav;
                if (!webDav.Enabled)
                {
                    return WriteValidation("WebDAV 云备份未启用，请先保存启用状态。");
                }

                if (!IsWebDavConfigured(webDav))
                {
                    return WriteValidation("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                return AcceptedBackgroundJob(EnqueueCloudBackupDownloadJob(
                    jobRunner,
                    user.Username,
                    remoteFileName));
            })
            .WithName("DownloadCloudDatabaseBackup")
            .WithApiCapability(PermissionResourceCatalog.SystemBackup, PermissionAction.Manage)
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);
        }

        private static ApiBackupListResponse CreateBackupListResponse(
            IBackupService backupService,
            IAppPathProvider pathProvider,
            bool revealPaths)
        {
            return new ApiBackupListResponse(
                ListBackups(backupService, revealPaths),
                ApiResponsePathPolicy.Reveal(pathProvider.BackupRoot, revealPaths),
                BackupStoragePolicy);
        }

        private static IReadOnlyList<ApiBackupItemDto> ListBackups(
            IBackupService backupService,
            bool revealPaths)
        {
            return (backupService.GetAvailableBackups() ?? [])
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .Select(file => new ApiBackupItemDto(
                    file.Name,
                    ApiResponsePathPolicy.Reveal(file.FullName, revealPaths),
                    file.Length,
                    new DateTimeOffset(file.CreationTime),
                    new DateTimeOffset(file.LastWriteTime)))
                .ToArray();
        }

        private static ApiCloudBackupItemDto ToCloudBackupItemDto(CloudBackupFileInfo file)
        {
            return new ApiCloudBackupItemDto(file.FileName, file.SizeBytes, file.LastModified);
        }

        private static bool TryResolveKnownBackupPath(
            IBackupService backupService,
            string requestedFileName,
            out string backupPath,
            out string errorMessage)
        {
            backupPath = string.Empty;
            errorMessage = string.Empty;
            string fileName = (requestedFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                errorMessage = "备份文件名不能为空。";
                return false;
            }

            if (fileName.Contains('/') ||
                fileName.Contains('\\') ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                errorMessage = "只能选择当前备份列表中的文件名，不能传入路径。";
                return false;
            }

            backupPath = (backupService.GetAvailableBackups() ?? [])
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    fileName,
                    PathBoundaryHelper.PathComparison)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                errorMessage = "未找到指定备份文件。";
                return false;
            }

            return true;
        }

        private static FileInfo? GetLatestBackupFile(IBackupService backupService)
        {
            return (backupService.GetAvailableBackups() ?? [])
                .Select(path => new FileInfo(path))
                .FirstOrDefault(file => file.Exists);
        }

        private static bool IsWebDavConfigured(WebDavSettings webDav)
        {
            return webDav != null &&
                !string.IsNullOrWhiteSpace(webDav.Url) &&
                !string.IsNullOrWhiteSpace(webDav.UserName);
        }

        private static bool TryNormalizeCloudBackupFileName(
            string requestedFileName,
            out string fileName,
            out string errorMessage)
        {
            fileName = (requestedFileName ?? string.Empty).Trim();
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                errorMessage = "云备份文件名不能为空。";
                return false;
            }

            if (!CrossPlatformFileNamePolicy.IsSafeFileName(fileName) ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                errorMessage = "只能选择 WebDAV 云备份列表中的文件名，不能传入路径。";
                return false;
            }

            if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "只能下载 ZIP 格式的数据库备份。";
                return false;
            }

            return true;
        }

    }
}

using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string BackupRestoreConfirmationText = "RESTORE";
        private const string BackupStoragePolicy =
            "数据库备份默认写入运行数据根 Backups/，只枚举和还原当前数据库对应的备份包；不读取发票/付款业务表，不生成导出目录，不写系统用户配置目录、全局程序数据目录或系统 C 盘默认落点。";
        private const string CloudBackupStoragePolicy =
            "WebDAV 云备份只读取程序根 appsettings.json 中已保存的 WebDAV 配置，并只上传运行数据根 Backups/ 中当前数据库对应的最新 ZIP 备份；不接受任意本地路径，不读取发票/报关业务表，也不读取付款/报销业务表。";

        private static void MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/backup", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                IAppPathProvider pathProvider) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看和管理数据库备份。");
                }

                return Results.Ok(CreateBackupListResponse(backupService, pathProvider));
            })
            .WithName("ListDatabaseBackups");

            endpoints.MapPost("/api/backup", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                IAppPathProvider pathProvider) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以创建数据库备份。");
                }

                await backupService.BackupDatabaseAsync();
                var list = CreateBackupListResponse(backupService, pathProvider);
                return Results.Ok(new ApiBackupCreateResponse(
                    true,
                    list.Backups.Count > 0 ? "数据库备份已创建。" : "未创建新的本地 SQLite 备份。",
                    list.Backups,
                    list.BackupRoot,
                    list.StoragePolicy));
            })
            .WithName("CreateDatabaseBackup");

            endpoints.MapPost("/api/backup/cleanup", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                IAppPathProvider pathProvider,
                ApiBackupCleanupRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

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
                var list = CreateBackupListResponse(backupService, pathProvider);
                return Results.Ok(new ApiBackupCreateResponse(
                    true,
                    request.DaysToKeep == 0 ? "保留天数为 0，未清理备份。" : "旧备份清理完成。",
                    list.Backups,
                    list.BackupRoot,
                    list.StoragePolicy));
            })
            .WithName("CleanupDatabaseBackups");

            endpoints.MapPost("/api/backup/restore", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackupService backupService,
                ApiBackupRestoreRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

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

                try
                {
                    backupService.RestoreDatabase(backupPath);
                    return Results.Ok(new ApiCommandResponse(true, "数据库已从备份还原，请重启桌面程序后继续使用。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("RestoreDatabaseBackup");

            endpoints.MapGet("/api/backup/cloud/status", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                IBackupService backupService,
                IAppPathProvider pathProvider) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看 WebDAV 云备份状态。");
                }

                await settingsService.LoadAsync();
                var webDav = settingsService.Settings?.WebDav ?? new WebDavSettings();
                var latestBackup = GetLatestBackupFile(backupService);
                return Results.Ok(new ApiCloudBackupStatusResponse(
                    webDav.Enabled,
                    IsWebDavConfigured(webDav),
                    webDav.Url?.Trim() ?? string.Empty,
                    webDav.UserName?.Trim() ?? string.Empty,
                    latestBackup?.Name ?? string.Empty,
                    latestBackup?.Length ?? 0,
                    pathProvider.BackupRoot,
                    CloudBackupStoragePolicy));
            })
            .WithName("GetCloudBackupStatus");

            endpoints.MapPost("/api/backup/cloud/test-connection", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ICloudSyncService cloudSyncService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以测试 WebDAV 云备份连接。");
                }

                await settingsService.LoadAsync();
                var webDav = settingsService.Settings?.WebDav ?? new WebDavSettings();
                if (!IsWebDavConfigured(webDav))
                {
                    return WriteConflict("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                bool success = await cloudSyncService.TestConnectionAsync(webDav);
                if (!success)
                {
                    return WriteConflict("WebDAV 连接测试失败，请检查地址、账号、密码或目录权限。");
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
            .WithName("TestCloudBackupConnection");

            endpoints.MapPost("/api/backup/cloud/upload-latest", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                IBackupService backupService,
                ICloudSyncService cloudSyncService,
                IAppPathProvider pathProvider) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以上传数据库备份到 WebDAV。");
                }

                await settingsService.LoadAsync();
                var webDav = settingsService.Settings?.WebDav ?? new WebDavSettings();
                if (!webDav.Enabled)
                {
                    return WriteConflict("WebDAV 云备份未启用，请先保存启用状态。");
                }

                if (!IsWebDavConfigured(webDav))
                {
                    return WriteConflict("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                var latestBackup = GetLatestBackupFile(backupService);
                if (latestBackup == null)
                {
                    return WriteConflict("当前没有可上传的数据库备份，请先创建本地备份。");
                }

                try
                {
                    await cloudSyncService.UploadFileAsync(latestBackup.FullName, latestBackup.Name);
                    return Results.Ok(new ApiCloudBackupCommandResponse(
                        true,
                        $"已上传最新数据库备份：{latestBackup.Name}",
                        latestBackup.Name,
                        latestBackup.FullName,
                        latestBackup.Length,
                        pathProvider.BackupRoot,
                        CloudBackupStoragePolicy));
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is ArgumentException ||
                    ex is IOException ||
                    ex is HttpRequestException)
                {
                    return WriteConflict($"WebDAV 云备份上传失败：{ex.Message}");
                }
            })
            .WithName("UploadLatestDatabaseBackupToCloud");

            endpoints.MapGet("/api/backup/cloud/backups", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ICloudSyncService cloudSyncService,
                IAppPathProvider pathProvider) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看 WebDAV 云备份列表。");
                }

                await settingsService.LoadAsync();
                var webDav = settingsService.Settings?.WebDav ?? new WebDavSettings();
                if (!webDav.Enabled)
                {
                    return WriteConflict("WebDAV 云备份未启用，请先保存启用状态。");
                }

                if (!IsWebDavConfigured(webDav))
                {
                    return WriteConflict("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                try
                {
                    var remoteBackups = await cloudSyncService.ListBackupFilesAsync();
                    return Results.Ok(new ApiCloudBackupListResponse(
                        remoteBackups.Select(ToCloudBackupItemDto).ToArray(),
                        pathProvider.BackupRoot,
                        CloudBackupStoragePolicy));
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is ArgumentException ||
                    ex is IOException ||
                    ex is HttpRequestException ||
                    ex is System.Xml.XmlException)
                {
                    return WriteConflict($"WebDAV 云备份列表读取失败：{ex.Message}");
                }
            })
            .WithName("ListCloudDatabaseBackups");

            endpoints.MapPost("/api/backup/cloud/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISettingsService settingsService,
                ICloudSyncService cloudSyncService,
                IAppPathProvider pathProvider,
                ApiCloudBackupDownloadRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

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

                await settingsService.LoadAsync();
                var webDav = settingsService.Settings?.WebDav ?? new WebDavSettings();
                if (!webDav.Enabled)
                {
                    return WriteConflict("WebDAV 云备份未启用，请先保存启用状态。");
                }

                if (!IsWebDavConfigured(webDav))
                {
                    return WriteConflict("WebDAV 尚未配置，请先保存服务器地址和用户名。");
                }

                try
                {
                    var remoteBackups = await cloudSyncService.ListBackupFilesAsync();
                    var selectedBackup = remoteBackups.FirstOrDefault(backup =>
                        string.Equals(backup.FileName, remoteFileName, StringComparison.OrdinalIgnoreCase));
                    if (selectedBackup == null)
                    {
                        return Results.BadRequest(new ApiErrorResponse("只能下载当前 WebDAV 云备份列表中的 ZIP 文件。"));
                    }

                    string localBackupPath = BuildLocalBackupPath(pathProvider.BackupRoot, remoteFileName);
                    await cloudSyncService.DownloadFileAsync(remoteFileName, localBackupPath);
                    var downloadedFile = new FileInfo(localBackupPath);
                    return Results.Ok(new ApiCloudBackupCommandResponse(
                        true,
                        $"已下载 WebDAV 云备份：{remoteFileName}",
                        remoteFileName,
                        downloadedFile.FullName,
                        downloadedFile.Exists ? downloadedFile.Length : selectedBackup.SizeBytes,
                        pathProvider.BackupRoot,
                        CloudBackupStoragePolicy));
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is ArgumentException ||
                    ex is IOException ||
                    ex is UnauthorizedAccessException ||
                    ex is HttpRequestException ||
                    ex is System.Xml.XmlException)
                {
                    return WriteConflict($"WebDAV 云备份下载失败：{ex.Message}");
                }
            })
            .WithName("DownloadCloudDatabaseBackup");
        }

        private static ApiBackupListResponse CreateBackupListResponse(
            IBackupService backupService,
            IAppPathProvider pathProvider)
        {
            return new ApiBackupListResponse(
                ListBackups(backupService),
                pathProvider.BackupRoot,
                BackupStoragePolicy);
        }

        private static IReadOnlyList<ApiBackupItemDto> ListBackups(IBackupService backupService)
        {
            return (backupService.GetAvailableBackups() ?? [])
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .Select(file => new ApiBackupItemDto(
                    file.Name,
                    file.FullName,
                    file.Length,
                    file.CreationTime,
                    file.LastWriteTime))
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

            if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                errorMessage = "只能选择当前备份列表中的文件名，不能传入路径。";
                return false;
            }

            backupPath = (backupService.GetAvailableBackups() ?? [])
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                errorMessage = "未找到指定备份文件。";
                return false;
            }

            return true;
        }

        private static FileInfo GetLatestBackupFile(IBackupService backupService)
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

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
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

        private static string BuildLocalBackupPath(string backupRoot, string fileName)
        {
            string fullBackupRoot = Path.GetFullPath(backupRoot);
            Directory.CreateDirectory(fullBackupRoot);
            string targetPath = Path.GetFullPath(Path.Combine(fullBackupRoot, fileName));
            string backupRootWithSeparator = fullBackupRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(backupRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("云备份下载目标必须位于运行数据根 Backups 目录内。");
            }

            return targetPath;
        }
    }
}

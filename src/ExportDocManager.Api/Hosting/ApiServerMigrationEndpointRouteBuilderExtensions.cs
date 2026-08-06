using System.Net;
using System.Security.Cryptography;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.AspNetCore.Http.Features;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        internal const string ServerMigrationPasswordHeader = "X-ExportDocManager-Migration-Password";
        internal const string ServerMigrationFileNameHeader = "X-ExportDocManager-Migration-File-Name";
        internal const string PostgreSqlBackupFileNameHeader = "X-ExportDocManager-PostgreSql-Backup-File-Name";
        internal const string RestoreConfirmationHeader = "X-ExportDocManager-Restore-Confirmation";
        internal const string SensitiveOperationTicketHeader = "X-ExportDocManager-Sensitive-Operation-Ticket";
        private const string AllowInsecureDisasterRecoveryEnvironmentVariable =
            "EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY";

        private static void MapServerMigrationEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/server-migration/status", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IServerMigrationService migrationService) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                ServerMigrationStatus status = migrationService.GetStatus();
                return Results.Ok(new ApiServerMigrationStatusResponse(
                    status.Supported,
                    status.PostgreSqlConfigured,
                    status.ToolsReady,
                    status.PendingRestore,
                    status.PackageRoot,
                    status.Message,
                    status.StoragePolicy,
                    status.RestorePhase,
                    status.RestoreDetail,
                    status.RestoreUpdatedAtUtc));
            })
            .WithName("GetServerMigrationStatus");

            endpoints.MapPost("/api/server-migration/authorization", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                ApiLoginAttemptService loginAttempts,
                ApiSensitiveOperationTicketService ticketService,
                IAppPathProvider pathProvider,
                ApiSensitiveOperationAuthorizationRequest request) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                string action = request?.Action?.Trim() ?? string.Empty;
                if (!ApiSensitiveOperationAction.IsKnown(action))
                    return Results.BadRequest(new ApiErrorResponse("敏感操作类型无效。"));
                IResult reauthenticationError = await ReauthenticateAsync(
                    context,
                    user,
                    request?.AdminPassword,
                    userService,
                    loginAttempts).ConfigureAwait(false);
                var requestContext = BuildRequestContext(context, user);
                if (reauthenticationError != null)
                {
                    ServerMigrationSecurityAudit.Write(
                        pathProvider,
                        $"authorize-{action}",
                        requestContext,
                        string.Empty,
                        success: false,
                        "管理员重新认证失败。");
                    return reauthenticationError;
                }
                ApiSensitiveOperationTicket ticket = ticketService.Issue(user.Id, action);
                ServerMigrationSecurityAudit.Write(
                    pathProvider,
                    $"authorize-{action}",
                    requestContext,
                    string.Empty,
                    success: true,
                    "已签发一次性敏感操作票据。");
                return Results.Ok(new ApiSensitiveOperationAuthorizationResponse(
                    action,
                    ticket.Token,
                    ticket.ExpiresAtUtc));
            })
            .WithName("AuthorizeServerMigrationOperation");

            endpoints.MapGet("/api/postgresql-maintenance/backups/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISharedDatabaseMaintenanceService maintenanceService) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                string fileName = context.Request.Query["fileName"].ToString();
                SharedDatabaseBackupItem backup = maintenanceService.ListPostgreSqlPhysicalBackups()
                    .FirstOrDefault(item => string.Equals(
                        item.FileName,
                        fileName,
                        StringComparison.OrdinalIgnoreCase));
                return backup == null
                    ? Results.NotFound(new ApiErrorResponse("未找到指定 PostgreSQL 备份。"))
                    : Results.File(
                        backup.FullPath,
                        "application/octet-stream",
                        backup.FileName,
                        enableRangeProcessing: true);
            })
            .WithName("DownloadPostgreSqlPhysicalBackup");

            endpoints.MapPost("/api/postgresql-maintenance/backups/restore", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                ApiLoginAttemptService loginAttempts,
                ISharedDatabaseMaintenanceService maintenanceService,
                IServerMigrationService migrationService,
                IHostApplicationLifetime applicationLifetime,
                ApiPostgreSqlDatabaseRestoreRequest request,
                CancellationToken cancellationToken) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                if (request == null || !string.Equals(
                    request.ConfirmationText?.Trim(),
                    "RESTORE DATABASE",
                    StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "恢复数据库前需要输入确认文本 RESTORE DATABASE。"));
                }
                IResult reauthenticationError = await ReauthenticateAsync(
                    context,
                    user,
                    request.AdminPassword,
                    userService,
                    loginAttempts).ConfigureAwait(false);
                if (reauthenticationError != null) return reauthenticationError;
                SharedDatabaseBackupItem backup = maintenanceService.ListPostgreSqlPhysicalBackups()
                    .FirstOrDefault(item => string.Equals(
                        item.FileName,
                        request.BackupFileName,
                        StringComparison.OrdinalIgnoreCase));
                if (backup == null)
                    return Results.NotFound(new ApiErrorResponse("未找到指定 PostgreSQL 备份。"));
                try
                {
                    await using var stream = new FileStream(
                        backup.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    ServerMigrationRestoreResult result = await migrationService
                        .StageDatabaseRestoreAsync(
                            stream,
                            backup.FileName,
                            BuildRequestContext(context, user),
                            cancellationToken)
                        .ConfigureAwait(false);
                    bool automaticRestart = ScheduleContainerRestart(context, applicationLifetime);
                    return ToRestoreResponse(result, automaticRestart);
                }
                catch (Exception ex) when (IsExpectedMigrationException(ex))
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("RestorePostgreSqlPhysicalBackup");

            endpoints.MapPost("/api/postgresql-maintenance/backups/upload-restore", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiSensitiveOperationTicketService ticketService,
                IServerMigrationService migrationService,
                IHostApplicationLifetime applicationLifetime,
                CancellationToken cancellationToken) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                if (!string.Equals(
                    context.Request.Headers[RestoreConfirmationHeader].ToString().Trim(),
                    "RESTORE DATABASE",
                    StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "恢复数据库前需要输入确认文本 RESTORE DATABASE。"));
                }
                if (!ticketService.Consume(
                    context.Request.Headers[SensitiveOperationTicketHeader].ToString(),
                    user.Id,
                    ApiSensitiveOperationAction.RestoreDatabase))
                {
                    return Results.Unauthorized();
                }
                ConfigureLargeUpload(context);
                string fileName = context.Request.Headers[PostgreSqlBackupFileNameHeader].ToString();
                try
                {
                    ServerMigrationRestoreResult result = await migrationService
                        .StageDatabaseRestoreAsync(
                            context.Request.Body,
                            fileName,
                            BuildRequestContext(context, user),
                            cancellationToken)
                        .ConfigureAwait(false);
                    bool automaticRestart = ScheduleContainerRestart(context, applicationLifetime);
                    return ToRestoreResponse(result, automaticRestart);
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (Exception ex) when (IsExpectedMigrationException(ex))
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UploadAndRestorePostgreSqlPhysicalBackup");

            endpoints.MapPost("/api/server-migration/packages", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                ApiLoginAttemptService loginAttempts,
                ApiBackgroundJobRunner jobRunner,
                IAppPathProvider pathProvider,
                ApiServerMigrationCreateRequest request) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                if (request == null || !string.Equals(
                    request.ConfirmationText?.Trim(),
                    "MIGRATE",
                    StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "创建完整迁移包前需要输入确认文本 MIGRATE。"));
                }
                IResult reauthenticationError = await ReauthenticateAsync(
                    context,
                    user,
                    request.AdminPassword,
                    userService,
                    loginAttempts).ConfigureAwait(false);
                if (reauthenticationError != null) return reauthenticationError;

                string packagePassword = request.Password ?? string.Empty;
                ServerMigrationRequestContext requestContext = BuildRequestContext(context, user);
                BackgroundJobSnapshot job = jobRunner.Enqueue(
                    "ServerMigrationPackage",
                    "创建服务器迁移包",
                    user.Username,
                    async (services, jobContext) =>
                    {
                        IServerMigrationService migrationService =
                            services.GetRequiredService<IServerMigrationService>();
                        jobContext.Report(5, "准备迁移数据", "正在创建 PostgreSQL 物理备份。");
                        ServerMigrationPackageResult result = null;
                        string outputPath = string.Empty;
                        try
                        {
                            result = await migrationService.CreatePackageAsync(
                                packagePassword,
                                requestContext,
                                jobContext.CancellationToken).ConfigureAwait(false);
                            jobContext.Report(95, "准备下载", "正在发布受控下载文件。");
                            outputPath = CreateBrowserDownloadPath(
                                pathProvider,
                                "server-migration",
                                result.FileName);
                            File.Move(result.FullPath, outputPath, overwrite: false);
                            return outputPath;
                        }
                        finally
                        {
                            if (result != null &&
                                !string.Equals(result.FullPath, outputPath, PathBoundaryHelper.PathComparison))
                            {
                                AtomicFileHelper.TryDeleteFile(result.FullPath);
                            }
                        }
                    });
                return Results.Accepted($"/api/jobs/{job.JobId}", job);
            })
            .WithName("CreateServerMigrationPackage");

            endpoints.MapPost("/api/server-migration/restore", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiSensitiveOperationTicketService ticketService,
                IServerMigrationService migrationService,
                IHostApplicationLifetime applicationLifetime,
                CancellationToken cancellationToken) =>
            {
                User user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageDisasterRecovery(user))
                    return WriteForbidden("当前账号没有灾难恢复管理权限。");
                IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                if (transportError != null) return transportError;
                if (!string.Equals(
                    context.Request.Headers[RestoreConfirmationHeader].ToString().Trim(),
                    "MIGRATE",
                    StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "恢复完整迁移包前需要输入确认文本 MIGRATE。"));
                }
                if (!ticketService.Consume(
                    context.Request.Headers[SensitiveOperationTicketHeader].ToString(),
                    user.Id,
                    ApiSensitiveOperationAction.RestoreServer))
                {
                    return Results.Unauthorized();
                }
                string password = context.Request.Headers[ServerMigrationPasswordHeader].ToString();
                string fileName = context.Request.Headers[ServerMigrationFileNameHeader].ToString();
                if (string.IsNullOrWhiteSpace(fileName))
                    return Results.BadRequest(new ApiErrorResponse("迁移包文件名不能为空。"));
                ConfigureLargeUpload(context);
                try
                {
                    ServerMigrationRestoreResult result = await migrationService.StageRestoreAsync(
                        context.Request.Body,
                        fileName,
                        password,
                        BuildRequestContext(context, user),
                        cancellationToken).ConfigureAwait(false);
                    bool automaticRestart = ScheduleContainerRestart(context, applicationLifetime);
                    return Results.Ok(new ApiServerMigrationRestoreResponse(
                        result.Success,
                        result.RestartRequired,
                        automaticRestart,
                        automaticRestart
                            ? "迁移恢复已排队，容器即将自动重启。"
                            : result.Message,
                        result.PackageFileName,
                        result.SafetyBackupRoot,
                        result.StoragePolicy));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (Exception ex) when (IsExpectedMigrationException(ex))
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("StageServerMigrationRestore");
        }

        private static async Task<IResult> ReauthenticateAsync(
            HttpContext context,
            User currentUser,
            string adminPassword,
            IUserService userService,
            ApiLoginAttemptService loginAttempts)
        {
            if (string.IsNullOrEmpty(adminPassword))
            {
                return Results.BadRequest(new ApiErrorResponse(
                    "执行灾难恢复前必须先为管理员账号设置非空密码，并输入当前密码重新认证。"));
            }
            string remoteAddress = GetRemoteAddress(context);
            ApiLoginAttemptDecision attempt = loginAttempts.Evaluate(
                currentUser.Username,
                remoteAddress);
            if (!attempt.Allowed)
            {
                SetRetryAfter(context, attempt.RetryAfter);
                return Results.Json(
                    new ApiErrorResponse("重新认证失败次数过多，请稍后再试。"),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
            User authenticated = await userService.AuthenticateAsync(
                currentUser.Username,
                adminPassword ?? string.Empty).ConfigureAwait(false);
            if (authenticated == null || authenticated.Id != currentUser.Id || !authenticated.IsActive)
            {
                ApiLoginAttemptDecision failure = loginAttempts.RecordFailure(
                    currentUser.Username,
                    remoteAddress);
                if (!failure.Allowed)
                {
                    SetRetryAfter(context, failure.RetryAfter);
                    return Results.Json(
                        new ApiErrorResponse("重新认证失败次数过多，请稍后再试。"),
                        statusCode: StatusCodes.Status429TooManyRequests);
                }
                return Results.Json(
                    new ApiErrorResponse("管理员当前密码不正确。"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            loginAttempts.RecordSuccess(currentUser.Username, remoteAddress);
            return null;
        }

        private static ServerMigrationRequestContext BuildRequestContext(
            HttpContext context,
            User user) =>
            new(user?.Username ?? string.Empty, GetRemoteAddress(context));

        private static string GetRemoteAddress(HttpContext context) =>
            context.Connection.RemoteIpAddress?.ToString() ?? "loopback";

        private static IResult RequireSecureDisasterRecoveryTransport(HttpContext context)
        {
            IPAddress remoteAddress = context.Connection.RemoteIpAddress;
            if (context.Request.IsHttps ||
                remoteAddress == null ||
                IPAddress.IsLoopback(remoteAddress) ||
                IsExplicitInsecureDisasterRecoveryOverrideEnabled())
            {
                return null;
            }
            return Results.Json(
                new ApiErrorResponse(
                    "灾难恢复操作默认只允许 HTTPS 或服务器本机回环管理通道。可信内网必须显式设置 EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=true。"),
                statusCode: StatusCodes.Status426UpgradeRequired);
        }

        private static bool IsExplicitInsecureDisasterRecoveryOverrideEnabled()
        {
            string value = Environment.GetEnvironmentVariable(
                AllowInsecureDisasterRecoveryEnvironmentVariable) ?? string.Empty;
            return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Trim() == "1";
        }

        private static bool IsExpectedMigrationException(Exception exception) => exception is
            InvalidOperationException or
            InvalidDataException or
            NotSupportedException or
            ArgumentException or
            CryptographicException or
            TimeoutException;

        private static void ConfigureLargeUpload(HttpContext context)
        {
            IHttpMaxRequestBodySizeFeature feature =
                context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = ApiUploadLimits.ServerMigrationPackageBytes;
            }
        }

        private static bool ScheduleContainerRestart(
            HttpContext context,
            IHostApplicationLifetime applicationLifetime)
        {
            bool automaticRestart = string.Equals(
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (!automaticRestart) return false;
            context.Response.OnCompleted(() =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    applicationLifetime.StopApplication();
                });
                return Task.CompletedTask;
            });
            return true;
        }

        private static IResult ToRestoreResponse(
            ServerMigrationRestoreResult result,
            bool automaticRestart) =>
            Results.Ok(new ApiServerMigrationRestoreResponse(
                result.Success,
                result.RestartRequired,
                automaticRestart,
                automaticRestart
                    ? "数据库恢复已排队，容器即将自动重启。"
                    : result.Message,
                result.PackageFileName,
                result.SafetyBackupRoot,
                result.StoragePolicy));
    }
}

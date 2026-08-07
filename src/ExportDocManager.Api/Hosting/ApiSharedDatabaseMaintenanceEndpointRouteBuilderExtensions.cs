using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string OwnershipTransferConfirmationText = "TRANSFER OWNERSHIP";
        private const string SensitiveSupportPackageJobKind = "SupportPackageWithOptionalFiles";

        private static void MapSharedDatabaseMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/postgresql-maintenance/backups", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISharedDatabaseMaintenanceService maintenanceService) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以查看 PostgreSQL 团队库备份。");
                }

                return Results.Ok(new ApiPostgreSqlPhysicalBackupListResponse(
                    ToPostgreSqlMaintenanceStatusResponse(maintenanceService.GetPostgreSqlMaintenanceStatus()),
                    maintenanceService.ListPostgreSqlPhysicalBackups().Select(ToSharedDatabaseBackupItemDto).ToArray()));
            })
            .WithName("ListPostgreSqlPhysicalBackups");

            endpoints.MapPost("/api/postgresql-maintenance/backups", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISharedDatabaseMaintenanceService maintenanceService,
                ApiBackgroundJobRunner jobRunner) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以创建 PostgreSQL 团队库物理备份。");
                }

                PostgreSqlMaintenanceStatus status = maintenanceService.GetPostgreSqlMaintenanceStatus();
                if (!status.PostgreSqlSelected || !status.PostgreSqlConfigured)
                {
                    return WriteValidation("当前未完整配置 PostgreSQL 团队数据库，不能创建物理备份。");
                }
                if (!status.ToolsReady)
                {
                    return WriteInfrastructureFailure(
                        "PostgreSQL 客户端工具未就绪，不能创建物理备份。",
                        new InvalidOperationException("PostgreSQL client tools are not ready."));
                }

                BackgroundJobSnapshot job = jobRunner.Enqueue(
                    "PostgreSqlPhysicalBackup",
                    "创建 PostgreSQL 物理备份",
                    user.Username,
                    async (services, jobContext) =>
                    {
                        ISharedDatabaseMaintenanceService scopedMaintenanceService =
                            services.GetRequiredService<ISharedDatabaseMaintenanceService>();
                        jobContext.Report(5, "准备数据库备份", "正在启动 pg_dump。可离开本页，任务会继续运行。");
                        PostgreSqlPhysicalBackupResult result = await scopedMaintenanceService
                            .CreatePostgreSqlPhysicalBackupAsync(jobContext.CancellationToken)
                            .ConfigureAwait(false);
                        jobContext.Report(
                            95,
                            "校验备份文件",
                            $"{result.FileName} 已写入运行目录，正在完成任务记录。");
                        return string.Empty;
                    });
                return AcceptedBackgroundJob(job);
            })
            .WithName("CreatePostgreSqlPhysicalBackup");

            endpoints.MapPost("/api/postgresql-maintenance/restore-plan", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISharedDatabaseMaintenanceService maintenanceService,
                ApiPostgreSqlRestorePlanRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以生成 PostgreSQL 团队库还原计划。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("PostgreSQL 还原计划请求体不能为空。"));
                }

                try
                {
                    var result = await maintenanceService.CreatePostgreSqlRestorePlanAsync(
                        new PostgreSqlRestorePlanRequest
                        {
                            BackupFileName = request.BackupFileName ?? string.Empty,
                            TargetDatabase = request.TargetDatabase ?? string.Empty,
                            ApplicationRole = request.ApplicationRole ?? string.Empty,
                            OldOwnerRoles = request.OldOwnerRoles ?? Array.Empty<string>()
                        },
                        cancellationToken).ConfigureAwait(false);
                    return Results.Ok(new ApiPostgreSqlRestorePlanResponse(
                        result.Success,
                        result.Message,
                        result.PlanRoot,
                        result.RestoreScriptPath,
                        result.OwnershipSqlPath,
                        result.BackupFilePath,
                        result.StoragePolicy));
                }
                catch (Exception ex) when (ex is ServiceException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("CreatePostgreSqlRestorePlan");

            endpoints.MapGet("/api/shared-database/ownership", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISharedDatabaseMaintenanceService maintenanceService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return WriteForbidden("只有管理员可以查看共享库归属统计。");
                }

                var summary = await maintenanceService.GetOwnershipSummaryAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(new ApiSharedDatabaseOwnershipSummaryResponse(
                    summary.TotalInvoices,
                    summary.UnassignedInvoices,
                    summary.TotalPayments,
                    summary.UnassignedPayments,
                    summary.TotalOtherBusinessData,
                    summary.UnassignedOtherBusinessData,
                    summary.Owners.Select(ToSharedDatabaseOwnerSummaryItemDto).ToArray(),
                    summary.StoragePolicy));
            })
            .WithName("GetSharedDatabaseOwnershipSummary");

            endpoints.MapPost("/api/shared-database/ownership/transfer", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ISharedDatabaseMaintenanceService maintenanceService,
                ApiSharedDatabaseOwnershipTransferRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return WriteForbidden("只有管理员可以执行共享库归属改派。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("归属改派请求体不能为空。"));
                }

                if (!string.Equals(request.ConfirmationText?.Trim(), OwnershipTransferConfirmationText, StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse($"归属改派前需要输入确认文本 {OwnershipTransferConfirmationText}。"));
                }

                try
                {
                    var result = await maintenanceService.TransferOwnershipAsync(
                        new SharedDatabaseOwnershipTransferRequest
                        {
                            FromUserId = request.FromUserId,
                            ToUserId = request.ToUserId,
                            IncludeInvoices = request.IncludeInvoices,
                            IncludePayments = request.IncludePayments,
                            IncludeOtherBusinessData = request.IncludeOtherBusinessData,
                            OnlyUnassigned = request.OnlyUnassigned,
                            DepartmentId = request.DepartmentId ?? string.Empty,
                            CompanyScope = request.CompanyScope ?? string.Empty
                        },
                        cancellationToken).ConfigureAwait(false);
                    return Results.Ok(new ApiSharedDatabaseOwnershipTransferResponse(
                        result.Success,
                        result.Message,
                        result.UpdatedInvoices,
                        result.UpdatedPayments,
                        result.UpdatedOtherBusinessData,
                        result.StoragePolicy));
                }
                catch (Exception ex) when (ex is ServiceException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("TransferSharedDatabaseOwnership");

            endpoints.MapPost("/api/support-package/save-to-runtime", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ISharedDatabaseMaintenanceService maintenanceService,
                ApiSupportPackageRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以导出支持包。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("将支持包保存到本机仅支持桌面版；浏览器版请直接下载支持包。");
                }

                var includeOptional = request?.IncludeLatestDatabaseBackup == true || request?.IncludeSampleFiles == true;
                if (includeOptional)
                {
                    IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                    if (transportError != null) return transportError;
                }
                if (includeOptional &&
                    !string.Equals(request?.ConfirmationText?.Trim(), "INCLUDE OPTIONAL FILES", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse("包含数据库备份或样张文件前需要输入确认文本 INCLUDE OPTIONAL FILES。"));
                }

                try
                {
                    var result = await maintenanceService.CreateSupportPackageAsync(
                        new SupportPackageOptions
                        {
                            IncludeLatestDatabaseBackup = request?.IncludeLatestDatabaseBackup == true,
                            IncludeSampleFiles = request?.IncludeSampleFiles == true
                        },
                        cancellationToken).ConfigureAwait(false);
                    return Results.Ok(new ApiSupportPackageResponse(
                        result.Success,
                        result.Message,
                        result.FileName,
                        result.FullPath,
                        result.SizeBytes,
                        result.SupportPackageRoot,
                        result.StoragePolicy));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("SaveSupportPackageToRuntime");

            endpoints.MapPost("/api/support-package/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiBackgroundJobRunner jobRunner,
                IAppPathProvider pathProvider,
                ApiSupportPackageRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以下载支持包。");
                }

                var includeOptional = request?.IncludeLatestDatabaseBackup == true || request?.IncludeSampleFiles == true;
                if (includeOptional)
                {
                    IResult transportError = RequireSecureDisasterRecoveryTransport(context);
                    if (transportError != null) return transportError;
                }
                if (includeOptional &&
                    !string.Equals(request?.ConfirmationText?.Trim(), "INCLUDE OPTIONAL FILES", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse("包含数据库备份或样张文件前需要输入确认文本 INCLUDE OPTIONAL FILES。"));
                }

                cancellationToken.ThrowIfCancellationRequested();
                var options = new SupportPackageOptions
                {
                    IncludeLatestDatabaseBackup = request?.IncludeLatestDatabaseBackup == true,
                    IncludeSampleFiles = request?.IncludeSampleFiles == true
                };
                BackgroundJobSnapshot job = jobRunner.Enqueue(
                    includeOptional ? SensitiveSupportPackageJobKind : "SupportPackage",
                    "创建技术支持包",
                    user.Username,
                    async (services, jobContext) =>
                    {
                        ISharedDatabaseMaintenanceService maintenanceService =
                            services.GetRequiredService<ISharedDatabaseMaintenanceService>();
                        jobContext.Report(5, "收集诊断信息", "正在生成脱敏支持包。可离开本页，任务会继续运行。");
                        SupportPackageResult result = null;
                        string outputPath = string.Empty;
                        try
                        {
                            result = await maintenanceService.CreateSupportPackageAsync(
                                options,
                                jobContext.CancellationToken).ConfigureAwait(false);
                            jobContext.Report(95, "准备下载", "正在发布受控支持包下载文件。");
                            outputPath = CreateBrowserDownloadPath(
                                pathProvider,
                                "support-packages",
                                result.FileName);
                            File.Move(result.FullPath, outputPath, overwrite: false);
                            jobContext.Report(99, "准备下载", "支持包已进入受控下载目录。", outputPath);
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
                return AcceptedBackgroundJob(job);
            })
            .WithName("DownloadSupportPackage");
        }

        private static ApiPostgreSqlMaintenanceStatusResponse ToPostgreSqlMaintenanceStatusResponse(
            PostgreSqlMaintenanceStatus status)
        {
            return new ApiPostgreSqlMaintenanceStatusResponse(
                status.PostgreSqlSelected,
                status.PostgreSqlConfigured,
                status.Host,
                status.Port,
                status.Database,
                status.Username,
                status.BackupRoot,
                status.ToolBinRoot,
                status.PgDumpPath,
                status.PgRestorePath,
                status.PsqlPath,
                status.ToolsReady,
                status.StoragePolicy);
        }

        private static ApiSharedDatabaseBackupItemDto ToSharedDatabaseBackupItemDto(SharedDatabaseBackupItem item)
        {
            if (item == null)
            {
                return new ApiSharedDatabaseBackupItemDto(string.Empty, string.Empty, 0, default, default);
            }

            return new ApiSharedDatabaseBackupItemDto(
                item.FileName,
                item.FullPath,
                item.SizeBytes,
                item.CreatedAt,
                item.LastWriteTime);
        }

        private static ApiSharedDatabaseOwnerSummaryItemDto ToSharedDatabaseOwnerSummaryItemDto(
            SharedDatabaseOwnerSummaryItem item)
        {
            return new ApiSharedDatabaseOwnerSummaryItemDto(
                item.UserId,
                item.Username,
                item.FullName,
                item.Role,
                item.DepartmentId,
                item.CompanyScope,
                item.IsActive,
                item.InvoiceCount,
                item.PaymentCount,
                item.OtherBusinessDataCount);
        }
    }
}

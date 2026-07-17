using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const int AuditLogDeleteMaxCount = 50000;
        private const int AuditLogCleanupMaxCount = 200000;
        private const string AuditLogStoragePolicy =
            "审计日志管理只读写运行数据根数据库 AuditLogs 表；Tauri 桌面保存只写用户显式选择的 .xlsx 路径，浏览器下载在内存生成且不在服务器创建导出副本，不读取发票/报关或付款/报销业务表。";

        private static void MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/audit-logs", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IAuditLogReadRepository auditLogReadRepository,
                int? pageNumber,
                int? pageSize,
                string invoiceKeyword,
                string entityName,
                string action,
                string userId,
                DateTime? startTime,
                DateTime? endTime,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageAuditLogs(user))
                {
                    return WriteForbidden("只有全功能版管理员可以查看审计日志。");
                }

                var result = await auditLogReadRepository.QueryPageAsync(
                    new AuditLogPageQuery
                    {
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50,
                        InvoiceKeyword = invoiceKeyword ?? string.Empty,
                        EntityName = entityName ?? string.Empty,
                        Action = action ?? string.Empty,
                        UserId = userId ?? string.Empty,
                        StartTime = startTime,
                        EndTime = endTime,
                        Keyword = keyword ?? string.Empty
                    },
                    cancellationToken);

                return Results.Ok(ApiAuditLogDtoFactory.FromPagedAuditLogs(result));
            })
            .WithName("ListAuditLogs");

            endpoints.MapPost("/api/audit-logs/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IAuditLogService auditLogService,
                ApiAuditLogPathExportRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageAuditLogs(user))
                {
                    return WriteForbidden("只有全功能版管理员可以导出审计日志。");
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径导出仅允许可信 Tauri 桌面端；浏览器请使用下载 Excel。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("审计日志导出请求体不能为空。"));
                }

                var validation = ValidateExcelDestinationPath(request.DestinationPath, "审计日志导出路径", out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                int exportedCount = await auditLogService.ExportToExcelAsync(
                    ToAuditLogCriteria(request),
                    destinationPath,
                    NormalizeMaxCount(request.MaxCount, AuditLogDeleteMaxCount),
                    cancellationToken);

                return Results.Ok(new ApiAuditLogCommandResponse(
                    true,
                    exportedCount > 0 ? "审计日志已导出。" : "当前条件下没有可导出的审计日志。",
                    exportedCount,
                    destinationPath,
                    AuditLogStoragePolicy));
            })
            .WithName("SaveAuditLogsToPath");

            endpoints.MapPost("/api/audit-logs/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IAuditLogService auditLogService,
                ApiAuditLogFilterRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageAuditLogs(user))
                {
                    return WriteForbidden("只有全功能版管理员可以下载审计日志。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("审计日志下载请求体不能为空。"));
                }

                byte[] content = await auditLogService.ExportToExcelBytesAsync(
                    ToAuditLogCriteria(request),
                    NormalizeMaxCount(request.MaxCount, AuditLogDeleteMaxCount),
                    cancellationToken);

                return Results.File(
                    content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"AuditLogs_{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
            })
            .WithName("DownloadAuditLogs");

            endpoints.MapPost("/api/audit-logs/delete", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IAuditLogService auditLogService,
                ApiAuditLogDeleteRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageAuditLogs(user))
                {
                    return WriteForbidden("只有全功能版管理员可以删除审计日志。");
                }

                var validationError = ValidateAuditLogDeleteRequest(request);
                if (validationError != null)
                {
                    return validationError;
                }

                int deletedCount = await auditLogService.DeleteByCriteriaAsync(
                    ToAuditLogCriteria(request),
                    NormalizeMaxCount(request.MaxCount, AuditLogDeleteMaxCount));

                return Results.Ok(new ApiAuditLogCommandResponse(
                    true,
                    $"已删除 {deletedCount} 条审计日志。",
                    deletedCount,
                    string.Empty,
                    AuditLogStoragePolicy));
            })
            .WithName("DeleteAuditLogsByCriteria");

            endpoints.MapPost("/api/audit-logs/cleanup", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IAuditLogService auditLogService,
                ApiAuditLogCleanupRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageAuditLogs(user))
                {
                    return WriteForbidden("只有全功能版管理员可以清理审计日志。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("审计日志清理请求体不能为空。"));
                }

                if (request.DaysToKeep <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("保留天数必须大于 0。"));
                }

                if (!request.Confirmed)
                {
                    return Results.BadRequest(new ApiErrorResponse("清理审计日志前必须明确确认操作。"));
                }

                var cutoffUtc = DateTime.UtcNow.AddDays(-request.DaysToKeep);
                int deletedCount = await auditLogService.DeleteOlderThanAsync(
                    cutoffUtc,
                    NormalizeMaxCount(request.MaxCount, AuditLogCleanupMaxCount));

                return Results.Ok(new ApiAuditLogCommandResponse(
                    true,
                    $"已清理 {deletedCount} 条早于 {request.DaysToKeep} 天的审计日志。",
                    deletedCount,
                    string.Empty,
                    AuditLogStoragePolicy));
            })
            .WithName("CleanupAuditLogs");
        }

        private static AuditLogQueryCriteria ToAuditLogCriteria(ApiAuditLogFilterRequest request)
        {
            return new AuditLogQueryCriteria
            {
                InvoiceKeyword = request?.InvoiceKeyword ?? string.Empty,
                EntityName = request?.EntityName ?? string.Empty,
                Action = request?.Action ?? string.Empty,
                UserId = request?.UserId ?? string.Empty,
                StartTime = request?.StartTime,
                EndTime = request?.EndTime,
                Keyword = request?.Keyword ?? string.Empty
            };
        }

        private static IResult ValidateAuditLogDeleteRequest(ApiAuditLogDeleteRequest request)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("审计日志删除请求体不能为空。"));
            }

            if (!request.Confirmed)
            {
                return Results.BadRequest(new ApiErrorResponse("删除审计日志前必须明确确认操作。"));
            }

            return HasAuditLogFilter(request)
                ? null
                : Results.BadRequest(new ApiErrorResponse("删除筛选结果前请至少设置一个筛选条件；如需维护历史日志，请使用按保留期清理。"));
        }

        private static bool HasAuditLogFilter(ApiAuditLogFilterRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.InvoiceKeyword)
                || !string.IsNullOrWhiteSpace(request.EntityName)
                || !string.IsNullOrWhiteSpace(request.Action)
                || !string.IsNullOrWhiteSpace(request.UserId)
                || request.StartTime.HasValue
                || request.EndTime.HasValue
                || !string.IsNullOrWhiteSpace(request.Keyword);
        }

        private static int NormalizeMaxCount(int maxCount, int hardLimit)
        {
            return Math.Clamp(maxCount <= 0 ? hardLimit : maxCount, 1, hardLimit);
        }
    }
}

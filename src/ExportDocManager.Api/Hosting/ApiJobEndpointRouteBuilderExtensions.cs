using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string BackgroundJobDownloadPurpose = "background-job";

        private static void MapJobEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/jobs", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IBackgroundJobService jobService,
                string? status,
                string? keyword,
                int? pageNumber,
                int? pageSize,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var result = await jobService.QueryAsync(
                    new BackgroundJobQuery
                    {
                        Status = status ?? string.Empty,
                        Keyword = keyword ?? string.Empty,
                        RequestedBy = authorizationService.CanViewAllBusinessData(user) ? string.Empty : user.Username,
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 20
                    },
                    cancellationToken);

                bool revealPaths = ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions);
                return Results.Ok(new ExportDocManager.Models.PagedResult<BackgroundJobSnapshot>(
                    result.Items.Select(job => ForJobClient(job, revealPaths)).ToList(),
                    result.TotalCount,
                    result.PageNumber,
                    result.PageSize));
            })
            .WithName("ListJobs")
            .Produces<ApiPagedResponse<BackgroundJobSnapshot>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/jobs/{jobId}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IBackgroundJobService jobService,
                string jobId,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(jobId))
                {
                    return Results.BadRequest(new ApiErrorResponse("任务 ID 不能为空。"));
                }

                var result = await jobService.GetAsync(jobId, cancellationToken);
                if (!CanAccessJob(result, user, authorizationService))
                {
                    return Results.NotFound();
                }

                return result == null
                    ? Results.NotFound()
                    : Results.Ok(ForJobClient(
                        result,
                        ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions)));
            })
            .WithName("GetJob")
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/api/jobs/{jobId}/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackgroundJobService jobService,
                IAppPathProvider pathProvider,
                string jobId,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(jobId))
                {
                    return Results.BadRequest(new ApiErrorResponse("任务 ID 不能为空。"));
                }

                var job = await jobService.GetAsync(jobId, cancellationToken);
                if (job == null ||
                    !CanAccessJob(job, user, authorizationService) ||
                    !string.Equals(job.Status, BackgroundJobStatusCatalog.Succeeded, StringComparison.OrdinalIgnoreCase) ||
                    !IsControlledBrowserDownloadPath(pathProvider, job.OutputPath) ||
                    !File.Exists(job.OutputPath))
                {
                    return Results.NotFound();
                }

                IResult? transportError = RequireSecureJobResultTransport(context, job);
                if (transportError != null) return transportError;

                string contentType = GetDownloadContentType(job.OutputPath);
                return Results.File(
                    job.OutputPath,
                    contentType,
                    Path.GetFileName(job.OutputPath),
                    enableRangeProcessing: true);
            })
            .WithName("DownloadJobResult")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status426UpgradeRequired);

            endpoints.MapPost("/api/jobs/{jobId}/download-ticket", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackgroundJobService jobService,
                IAppPathProvider pathProvider,
                ApiDownloadTicketService ticketService,
                ApiDesktopAccessOptions desktopAccessOptions,
                string jobId,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var job = await jobService.GetAsync(jobId, cancellationToken);
                if (job == null ||
                    !CanAccessJob(job, user, authorizationService) ||
                    !string.Equals(job.Status, BackgroundJobStatusCatalog.Succeeded, StringComparison.OrdinalIgnoreCase) ||
                    !IsControlledBrowserDownloadPath(pathProvider, job.OutputPath) ||
                    !File.Exists(job.OutputPath))
                {
                    return Results.NotFound();
                }

                IResult? transportError = RequireSecureJobResultTransport(context, job);
                if (transportError != null) return transportError;

                return Results.Ok(ticketService.Issue(
                    context,
                    BackgroundJobDownloadPurpose,
                    job.JobId,
                    user.Id.ToString(),
                    "/downloads/jobs",
                    requireSessionBinding: !ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions)));
            })
            .WithName("CreateJobDownloadTicket")
            .Produces<ApiDownloadTicket>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status426UpgradeRequired);

            endpoints.MapGet("/downloads/jobs/{token}", async (
                HttpContext context,
                ApiDownloadTicketService ticketService,
                IBackgroundJobService jobService,
                IAppPathProvider pathProvider,
                string token,
                CancellationToken cancellationToken) =>
            {
                if (!ticketService.TryResolve(
                    context,
                    token,
                    BackgroundJobDownloadPurpose,
                    out string jobId))
                {
                    return Results.NotFound();
                }

                var job = await jobService.GetAsync(jobId, cancellationToken);
                if (job == null ||
                    !string.Equals(job.Status, BackgroundJobStatusCatalog.Succeeded, StringComparison.OrdinalIgnoreCase) ||
                    !IsControlledBrowserDownloadPath(pathProvider, job.OutputPath) ||
                    !File.Exists(job.OutputPath))
                {
                    return Results.NotFound();
                }

                IResult? transportError = RequireSecureJobResultTransport(context, job);
                if (transportError != null) return transportError;

                return Results.File(
                    job.OutputPath,
                    GetDownloadContentType(job.OutputPath),
                    Path.GetFileName(job.OutputPath),
                    enableRangeProcessing: true);
            })
            .WithName("DownloadJobResultWithTicket")
            .WithApiAccess(false, true, false)
            .AllowApiWithoutPermission()
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status426UpgradeRequired);

            endpoints.MapPost("/api/jobs/{jobId}/cancel", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackgroundJobService jobService,
                string jobId,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(jobId))
                {
                    return Results.BadRequest(new ApiErrorResponse("任务 ID 不能为空。"));
                }

                var job = await jobService.GetAsync(jobId, cancellationToken);
                if (!CanAccessJob(job, user, authorizationService))
                {
                    return Results.Json(
                        new ApiErrorResponse("任务不存在、已结束或不支持取消。"),
                        statusCode: StatusCodes.Status409Conflict);
                }

                bool accepted = await jobService.RequestCancelAsync(jobId, cancellationToken);
                return accepted
                    ? Results.Ok(new ApiCommandResponse(true, "已请求取消任务。"))
                    : Results.Json(
                        new ApiErrorResponse("任务不存在、已结束或不支持取消。"),
                        statusCode: StatusCodes.Status409Conflict);
            })
            .WithName("CancelJob")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/jobs/{jobId}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackgroundJobService jobService,
                string jobId,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(jobId))
                {
                    return Results.BadRequest(new ApiErrorResponse("任务 ID 不能为空。"));
                }

                var job = await jobService.GetAsync(jobId, cancellationToken);
                if (!CanAccessJob(job, user, authorizationService))
                {
                    return Results.Json(
                        new ApiErrorResponse("任务不存在，或仍在排队/运行中，不能删除。"),
                        statusCode: StatusCodes.Status409Conflict);
                }

                bool deleted = await jobService.DeleteAsync(jobId, cancellationToken);
                return deleted
                    ? Results.Ok(new ApiCommandResponse(true, "已删除任务记录。"))
                    : Results.Json(
                        new ApiErrorResponse("任务不存在，或仍在排队/运行中，不能删除。"),
                        statusCode: StatusCodes.Status409Conflict);
            })
            .WithName("DeleteJob")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/jobs/finished", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackgroundJobService jobService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                string requestedBy = authorizationService.CanViewAllBusinessData(user) ? string.Empty : user.Username;
                int deletedCount = await jobService.ClearTerminalAsync(requestedBy, cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已清理 {deletedCount} 条已结束任务记录。"));
            })
            .WithName("ClearFinishedJobs")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/jobs/{jobId}/retry", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IBackgroundJobService jobService,
                ApiBackgroundJobRetryDispatcher retryDispatcher,
                IInvoiceService invoiceService,
                string jobId,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(jobId))
                {
                    return Results.BadRequest(new ApiErrorResponse("任务 ID 不能为空。"));
                }

                var sourceJob = await jobService.GetAsync(jobId, cancellationToken);
                if (sourceJob == null || !CanAccessJob(sourceJob, user, authorizationService))
                {
                    return Results.NotFound();
                }

                return await retryDispatcher.RetryAsync(
                    sourceJob,
                    user,
                    authorizationService,
                    invoiceService,
                    cancellationToken);
            })
            .WithName("RetryJob")
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        }

        private static IResult? RequireSecureJobResultTransport(
            HttpContext context,
            BackgroundJobSnapshot job)
        {
            if (job == null ||
                (!string.Equals(job.Kind, ServerMigrationPackageJobKind, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(job.Kind, SensitiveSupportPackageJobKind, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return RequireSecureDisasterRecoveryTransport(context);
        }

        private static bool CanAccessJob(
            BackgroundJobSnapshot? job,
            User user,
            ApiAuthorizationService authorizationService)
        {
            if (job == null || user == null)
            {
                return false;
            }

            return authorizationService.CanViewAllBusinessData(user) ||
                string.Equals(job.RequestedBy, user.Username, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDownloadContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".zip" => "application/zip",
                ".edpkg" => "application/octet-stream",
                ".swpkg" => "application/octet-stream",
                ".edmmigration" => "application/octet-stream",
                _ => "application/octet-stream"
            };
        }

        private static BackgroundJobSnapshot ForJobClient(BackgroundJobSnapshot job, bool revealPaths)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (revealPaths || string.IsNullOrWhiteSpace(job.OutputPath))
            {
                return job;
            }

            return new BackgroundJobSnapshot
            {
                JobId = job.JobId,
                Kind = job.Kind,
                Title = job.Title,
                Status = job.Status,
                ProgressPercent = job.ProgressPercent,
                StatusText = job.StatusText,
                DetailText = job.DetailText,
                RequestedBy = job.RequestedBy,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                UpdatedAt = job.UpdatedAt,
                OutputPath = Path.GetFileName(job.OutputPath),
                ErrorMessage = job.ErrorMessage,
                CanCancel = job.CanCancel,
                CanRetry = job.CanRetry,
                RetryOperation = job.RetryOperation,
                RetryRequestJson = string.Empty
            };
        }
    }
}

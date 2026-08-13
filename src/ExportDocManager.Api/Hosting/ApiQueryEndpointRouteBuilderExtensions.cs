using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string QueryInvoiceStoragePolicy =
            "发票/报关查询只读取运行数据根数据库中的 Invoices 与 Items 数据；不读取或关联付款/报销业务表。Excel 导出只写用户显式选择的 .xlsx 路径，sidecar 不分配默认导出目录。";

        private static void MapQueryEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/query/invoices", async Task<Results<
                Ok<ApiPagedResponse<ApiQueryInvoiceRowDto>>,
                UnauthorizedHttpResult>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                IQueryReadRepository queryReadRepository,
                DateOnly? startDate,
                DateOnly? endDateExclusive,
                int? customerId,
                int? exporterId,
                string? keyword,
                string? contractNo,
                string? invoiceType,
                string? transportMode,
                string? styleName,
                string? styleNo,
                int? pageNumber,
                int? pageSize,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return TypedResults.Unauthorized();
                }

                var result = await queryReadRepository.QueryPageAsync(
                    new QueryPageQuery
                    {
                        StartDate = startDate,
                        EndDateExclusive = endDateExclusive,
                        CustomerId = customerId,
                        ExporterId = exporterId,
                        Keyword = keyword ?? string.Empty,
                        ContractNo = contractNo ?? string.Empty,
                        InvoiceType = invoiceType ?? string.Empty,
                        TransportMode = transportMode ?? string.Empty,
                        StyleName = styleName ?? string.Empty,
                        StyleNo = styleNo ?? string.Empty,
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50
                    },
                    cancellationToken);

                return TypedResults.Ok(ApiQueryDtoFactory.FromPagedInvoices(result));
            })
            .WithName("ListQueriedInvoices");

            endpoints.MapPost("/api/query/invoices/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiQueryInvoiceExportRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("该本机导出操作仅支持桌面版；浏览器版请直接下载 Excel。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("查询结果导出请求体不能为空。"));
                }

                var validation = ValidateExcelDestinationPath(request.DestinationPath, "查询结果导出路径", out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueQueryInvoiceExportJob(
                    jobRunner,
                    user.Username,
                    request,
                    destinationPath));
            })
            .WithName("SaveQueriedInvoicesToPath")
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/query/invoices/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                ApiQueryInvoiceFilterRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("查询结果下载请求体不能为空。"));
                }

                string destinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "QueryExport",
                    $"QueryResults_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
                return AcceptedBackgroundJob(EnqueueQueryInvoiceExportJob(
                    jobRunner,
                    user.Username,
                    request,
                    destinationPath));
            })
            .WithName("DownloadQueriedInvoices")
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        }

        internal static BackgroundJobSnapshot EnqueueQueryInvoiceExportJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            ApiQueryInvoiceFilterRequest request,
            string destinationPath)
        {
            var retryRequest = new ApiQueryInvoiceExportRequest
            {
                StartDate = request?.StartDate,
                EndDateExclusive = request?.EndDateExclusive,
                CustomerId = request?.CustomerId,
                ExporterId = request?.ExporterId,
                Keyword = request?.Keyword ?? string.Empty,
                ContractNo = request?.ContractNo ?? string.Empty,
                InvoiceType = request?.InvoiceType ?? string.Empty,
                TransportMode = request?.TransportMode ?? string.Empty,
                StyleName = request?.StyleName ?? string.Empty,
                StyleNo = request?.StyleNo ?? string.Empty,
                DestinationPath = destinationPath
            };

            return jobRunner.Enqueue(
                "QueryInvoiceExcelExport",
                "导出查询结果 Excel",
                requestedBy,
                async (provider, jobContext) =>
                {
                    jobContext.Report(5, "正在准备查询结果", QueryInvoiceStoragePolicy, destinationPath);
                    var progress = new InlineProgress<OperationProgressUpdate>(update =>
                    {
                        jobContext.Report(
                            Math.Clamp(update.ProgressPercent ?? 5, 5, 98),
                            update.StatusText,
                            update.DetailText,
                            destinationPath);
                    });
                    var exportService = provider.GetRequiredService<IQueryResultExportService>();
                    var result = await exportService.ExportToExcelAsync(
                            ToQueryPageQuery(retryRequest),
                            destinationPath,
                            progress,
                            jobContext.CancellationToken)
                        .ConfigureAwait(false);

                    jobContext.Report(
                        99,
                        "正在保存查询结果",
                        result.ExportedCount > 0
                            ? $"已导出 {result.ExportedCount} 条记录。"
                            : "当前条件下没有记录，已生成仅含表头的 Excel。",
                        result.DestinationPath);
                    return result.DestinationPath;
                },
                retryOperation: "StartQueryInvoiceExportJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(retryRequest),
                initialOutputPath: destinationPath);
        }

        private static QueryPageQuery ToQueryPageQuery(ApiQueryInvoiceFilterRequest request)
        {
            return new QueryPageQuery
            {
                StartDate = request?.StartDate,
                EndDateExclusive = request?.EndDateExclusive,
                CustomerId = request?.CustomerId,
                ExporterId = request?.ExporterId,
                Keyword = request?.Keyword ?? string.Empty,
                ContractNo = request?.ContractNo ?? string.Empty,
                InvoiceType = request?.InvoiceType ?? string.Empty,
                TransportMode = request?.TransportMode ?? string.Empty,
                StyleName = request?.StyleName ?? string.Empty,
                StyleNo = request?.StyleNo ?? string.Empty
            };
        }
    }
}

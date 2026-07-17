using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string QueryInvoiceStoragePolicy =
            "发票/报关查询只读取运行数据根数据库中的 Invoices 与 Items 数据；不读取或关联付款/报销业务表。Excel 导出只写用户显式选择的 .xlsx 路径，sidecar 不分配默认导出目录。";

        private static void MapQueryEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/query/invoices", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IQueryReadRepository queryReadRepository,
                DateTime? startDate,
                DateTime? endDate,
                int? customerId,
                int? exporterId,
                string keyword,
                string contractNo,
                string invoiceType,
                string transportMode,
                string styleName,
                string styleNo,
                int? pageNumber,
                int? pageSize,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var result = await queryReadRepository.QueryPageAsync(
                    new QueryPageQuery
                    {
                        StartDate = startDate,
                        EndDate = endDate,
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

                return Results.Ok(ApiQueryDtoFactory.FromPagedInvoices(result));
            })
            .WithName("ListQueriedInvoices");

            endpoints.MapPost("/api/query/invoices/save-to-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IQueryResultExportService queryResultExportService,
                ApiQueryInvoiceExportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径导出仅允许可信 Tauri 桌面端；浏览器请使用下载 Excel。");
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

                var result = await queryResultExportService.ExportToExcelAsync(
                    ToQueryPageQuery(request),
                    destinationPath,
                    cancellationToken: cancellationToken);

                return Results.Ok(new ApiQueryInvoiceExportResponse(
                    true,
                    result.ExportedCount > 0 ? "查询结果已导出。" : "当前条件下没有可导出的查询结果。",
                    result.ExportedCount,
                    result.DestinationPath,
                    QueryInvoiceStoragePolicy));
            })
            .WithName("SaveQueriedInvoicesToPath");

            endpoints.MapPost("/api/query/invoices/download", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IQueryResultExportService queryResultExportService,
                ApiQueryInvoiceFilterRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("查询结果下载请求体不能为空。"));
                }

                var result = await queryResultExportService.ExportToExcelBytesAsync(
                    ToQueryPageQuery(request),
                    cancellationToken);
                return Results.File(
                    result.Content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"QueryResults_{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
            })
            .WithName("DownloadQueriedInvoices");
        }

        private static QueryPageQuery ToQueryPageQuery(ApiQueryInvoiceFilterRequest request)
        {
            return new QueryPageQuery
            {
                StartDate = request?.StartDate,
                EndDate = request?.EndDate,
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

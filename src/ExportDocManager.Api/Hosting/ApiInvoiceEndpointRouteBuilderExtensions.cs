using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/invoices", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceListReadRepository invoiceListReadRepository,
                int? pageNumber,
                int? pageSize,
                string keyword,
                string sortColumn,
                bool? ascending,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var result = await invoiceListReadRepository.QueryPageAsync(
                    new InvoiceListPageQuery
                    {
                        PageNumber = pageNumber ?? 1,
                        PageSize = pageSize ?? 50,
                        Keyword = keyword ?? string.Empty,
                        SortColumn = sortColumn ?? string.Empty,
                        Ascending = ascending ?? false
                    },
                    cancellationToken);

                return Results.Ok(ApiInvoiceDtoFactory.FromPagedInvoices(result));
            })
            .WithName("ListInvoices");

            endpoints.MapGet("/api/invoices/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                var invoice = await invoiceService.GetInvoiceByIdAsync(id);
                return invoice == null
                    ? Results.NotFound()
                    : Results.Ok(ApiInvoiceDtoFactory.FromInvoiceDetail(invoice));
            })
            .WithName("GetInvoice");

            endpoints.MapPost("/api/invoices/profit-analysis", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceProfitAnalysisService profitAnalysisService,
                ApiInvoiceProfitAnalysisRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request?.Invoice == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("利润分析发票草稿不能为空。"));
                }

                try
                {
                    var invoice = ApiInvoiceDtoFactory.ToInvoiceForSave(request.Invoice);
                    var result = profitAnalysisService.Analyze(invoice);
                    return Results.Ok(ToProfitAnalysisResponse(result));
                }
                catch (FormatException)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票 rowVersion 必须是有效的 Base64 字符串。"));
                }
            })
            .WithName("AnalyzeInvoiceProfit");

            endpoints.MapPost("/api/invoices", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                ApiInvoiceDetailDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增发票不能包含已有ID。"));
                }

                if (string.IsNullOrWhiteSpace(request.InvoiceNo))
                {
                    return Results.BadRequest(new ApiErrorResponse("发票号不能为空。"));
                }

                Invoice invoice;
                try
                {
                    invoice = ApiInvoiceDtoFactory.ToInvoiceForSave(request);
                }
                catch (FormatException)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票 rowVersion 必须是有效的 Base64 字符串。"));
                }

                invoice.Id = 0;
                invoice.OwnerUserId = null;
                invoice.DepartmentId = string.Empty;
                invoice.CompanyScope = string.Empty;

                var result = await invoiceService.SaveInvoiceWithAutoCreationAsync(
                    invoice,
                    invoice.Items?.ToList() ?? new List<Item>(),
                    ApiInvoiceDtoFactory.CreateCustomerForAutoCreation(invoice),
                    ApiInvoiceDtoFactory.CreateExporterForAutoCreation(invoice));

                if (!result.Success || result.SavedInvoice == null)
                {
                    return WriteConflict(result.ErrorMessage ?? "保存发票失败。");
                }

                var response = new ApiInvoiceSaveResponse(
                    true,
                    result.SavedInvoice.Id,
                    result.IsUpdate,
                    ApiInvoiceDtoFactory.FromInvoiceDetail(result.SavedInvoice));
                return Results.Created($"/api/invoices/{result.SavedInvoice.Id}", response);
            })
            .WithName("CreateInvoice");

            endpoints.MapPut("/api/invoices/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                int id,
                ApiInvoiceDetailDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体发票ID与路径ID不一致。"));
                }

                if (string.IsNullOrWhiteSpace(request.InvoiceNo))
                {
                    return Results.BadRequest(new ApiErrorResponse("发票号不能为空。"));
                }

                var existing = await invoiceService.GetInvoiceByIdAsync(id);
                if (existing == null)
                {
                    return Results.NotFound();
                }

                Invoice invoice;
                try
                {
                    invoice = ApiInvoiceDtoFactory.ToInvoiceForSave(request);
                }
                catch (FormatException)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票 rowVersion 必须是有效的 Base64 字符串。"));
                }

                invoice.Id = id;
                ApiInvoiceDtoFactory.PreserveExistingOwnership(invoice, existing);

                var result = await invoiceService.SaveInvoiceWithAutoCreationAsync(
                    invoice,
                    invoice.Items?.ToList() ?? new List<Item>(),
                    ApiInvoiceDtoFactory.CreateCustomerForAutoCreation(invoice),
                    ApiInvoiceDtoFactory.CreateExporterForAutoCreation(invoice));

                if (!result.Success || result.SavedInvoice == null)
                {
                    return WriteConflict(result.ErrorMessage ?? "保存发票失败。");
                }

                return Results.Ok(new ApiInvoiceSaveResponse(
                    true,
                    result.SavedInvoice.Id,
                    result.IsUpdate,
                    ApiInvoiceDtoFactory.FromInvoiceDetail(result.SavedInvoice)));
            })
            .WithName("UpdateInvoice");

            endpoints.MapDelete("/api/invoices/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                bool deleted;
                try
                {
                    deleted = await invoiceService.DeleteInvoiceAsync(id);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }

                return deleted
                    ? Results.Ok(new ApiCommandResponse(true, "发票已删除。"))
                    : Results.NotFound();
            })
            .WithName("DeleteInvoice");

            endpoints.MapPost("/api/invoices/{id:int}/unverify", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                Invoice invoice;
                try
                {
                    invoice = await invoiceService.UnverifyInvoiceAsync(id);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }

                return invoice == null
                    ? Results.NotFound()
                    : Results.Ok(new ApiInvoiceSaveResponse(
                        true,
                        invoice.Id,
                        true,
                        ApiInvoiceDtoFactory.FromInvoiceDetail(invoice)));
            })
            .WithName("UnverifyInvoice");

            endpoints.MapPost("/api/invoices/{id:int}/clone", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                int id,
                ApiInvoiceCloneRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                if (request == null || string.IsNullOrWhiteSpace(request.NewInvoiceNo))
                {
                    return Results.BadRequest(new ApiErrorResponse("新发票号不能为空。"));
                }

                Invoice copiedInvoice;
                try
                {
                    copiedInvoice = await invoiceService.CopyInvoiceAsync(
                        id,
                        request.NewInvoiceNo.Trim(),
                        request.Options ?? new InvoiceCloneOptions());
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }

                return copiedInvoice == null
                    ? Results.NotFound()
                    : Results.Ok(new ApiInvoiceCloneResponse(
                        true,
                        copiedInvoice.Id,
                        ApiInvoiceDtoFactory.FromInvoiceDetail(copiedInvoice),
                        "发票已复制。"));
            })
            .WithName("CloneInvoice");

            endpoints.MapPost("/api/invoices/{id:int}/clone-type", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IInvoiceService invoiceService,
                int id,
                ApiInvoiceCloneTypeRequest request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                if (request == null || string.IsNullOrWhiteSpace(request.TargetType))
                {
                    return Results.BadRequest(new ApiErrorResponse("目标发票类型不能为空。"));
                }

                var targetType = request.TargetType.Trim();
                if (!Array.Exists(AppConstants.TradeTypes, value => string.Equals(value, targetType, StringComparison.Ordinal)))
                {
                    return Results.BadRequest(new ApiErrorResponse("目标发票类型只能是实际数据或报关数据。"));
                }

                var source = await invoiceService.GetInvoiceByIdAsync(id);
                if (source == null)
                {
                    return Results.NotFound();
                }

                if (string.Equals(source.Type?.Trim(), targetType, StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse("目标发票类型必须与源发票类型不同。"));
                }

                var existingTarget = await invoiceService.GetInvoiceByInvoiceNoAndTypeAsync(source.InvoiceNo, targetType);
                if (existingTarget != null)
                {
                    return Results.Conflict(new ApiErrorResponse($"同一发票号的{targetType}已存在，未覆盖。"));
                }

                var options = request.Options ?? new InvoiceCloneOptions
                {
                    CopyHeader = true,
                    CopyItems = true,
                    ResetDates = false,
                    ResetStatus = true,
                    ClearAmounts = false
                };

                Invoice clonedInvoice;
                try
                {
                    clonedInvoice = await invoiceService.CopyInvoiceAsTypeAsync(id, targetType, options);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }

                return clonedInvoice == null
                    ? Results.NotFound()
                    : Results.Ok(new ApiInvoiceCloneTypeResponse(
                        true,
                        clonedInvoice.Id,
                        ApiInvoiceDtoFactory.FromInvoiceDetail(clonedInvoice),
                        $"已生成同一发票号的{targetType}。"));
            })
            .WithName("CloneInvoiceAsType");
        }

        private static ApiInvoiceProfitAnalysisResponse ToProfitAnalysisResponse(
            InvoiceProfitAnalysisResult result)
        {
            return new ApiInvoiceProfitAnalysisResponse(
                result.Currency,
                result.SalesTotal,
                result.ExchangeRate,
                result.SalesRmb,
                result.PurchaseCost,
                result.TaxRefund,
                result.GrossProfit,
                result.Margin,
                result.SalesTotalText,
                result.ExchangeRateText,
                result.SalesRmbText,
                result.PurchaseCostText,
                result.TaxRefundText,
                result.GrossProfitText,
                result.MarginText,
                "利润分析只使用当前请求中的发票/发票草稿字段进行内存计算；不读取付款/报销单据，不写数据库、不生成文件、不创建默认输出目录或系统 C 盘落点。");
        }
    }
}

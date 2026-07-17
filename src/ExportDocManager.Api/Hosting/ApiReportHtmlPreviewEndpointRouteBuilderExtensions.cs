using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string InvoiceDocumentPackagePreviewStoragePolicy =
            "单据包 HTML 预览只读取当前发票/报关数据域和程序根 Templates 模板；HTML 仅随响应返回到前端内存，不生成 PDF/ZIP，不写运行缓存、数据库、默认导出目录、系统用户数据目录或系统 C 盘默认路径。";

        private const string InvoiceDraftPreviewStoragePolicy =
            "发票草稿 HTML 预览只使用请求体中的当前发票/报关草稿、程序根 Templates 模板以及客户/出口商主数据快照；不按发票号读取付款/报销单据或另一种发票口径，不写数据库、缓存、默认导出目录、系统用户数据目录或系统 C 盘默认路径。";

        private const string PaymentDraftPreviewStoragePolicy =
            "付款/报销草稿 HTML 预览只使用请求体中的付款/报销草稿、程序根 Templates/Internal 模板以及收款对象/出口商主数据；不按 Payment.InvoiceNo 读取发票/报关单据，不写数据库、缓存、默认导出目录、系统用户数据目录或系统 C 盘默认路径。";

        private static void MapInvoiceReportHtmlPreviewEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/html-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportHtmlService reportHtmlService,
                int invoiceId,
                ApiReportHtmlPreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                request ??= new ApiReportHtmlPreviewRequest();
                if (!TryParseReportDocumentType(request.ReportType, out var reportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                try
                {
                    var result = await reportHtmlService.RenderInvoiceReportAsync(
                        invoiceId,
                        reportType,
                        request.TemplatePath,
                        request.WithSeal,
                        cancellationToken);

                    return Results.Ok(new ApiReportHtmlPreviewResponse(
                        result.SourceId,
                        result.ReportType.ToString(),
                        result.TemplatePath,
                        result.WithSeal,
                        result.Html));
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewInvoiceReportHtml");

            endpoints.MapPost("/api/reports/invoices/draft/html-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportHtmlService reportHtmlService,
                ApiInvoiceDraftReportHtmlPreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票草稿预览请求体不能为空。"));
                }

                if (request.Invoice == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票草稿不能为空。"));
                }

                var requestedReportType = string.IsNullOrWhiteSpace(request.ReportType)
                    ? ReportDocumentType.ExportDocument.ToString()
                    : request.ReportType;
                if (!TryParseReportDocumentType(requestedReportType, out var reportType)
                    || reportType != ReportDocumentType.ExportDocument)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票草稿 HTML 预览仅支持 ExportDocument 报表类型。"));
                }

                try
                {
                    var invoice = ApiInvoiceDtoFactory.ToInvoiceForSave(request.Invoice);
                    var result = await reportHtmlService.RenderInvoiceReportDraftAsync(
                        invoice,
                        reportType,
                        request.TemplatePath,
                        request.WithSeal,
                        cancellationToken);

                    return Results.Ok(new ApiReportHtmlPreviewResponse(
                        result.SourceId,
                        result.ReportType.ToString(),
                        result.TemplatePath,
                        result.WithSeal,
                        result.Html,
                        InvoiceDraftPreviewStoragePolicy));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewInvoiceReportDraftHtml");
        }

        private static void MapInvoiceDocumentPackageHtmlPreviewEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/document-package/html-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportHtmlService reportHtmlService,
                int invoiceId,
                ApiInvoiceDocumentPackagePreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (invoiceId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
                }

                request ??= new ApiInvoiceDocumentPackagePreviewRequest();
                var itemValidation = ValidateInvoiceDocumentItemRequests(request.Items, out var normalizedItems);
                if (itemValidation != null)
                {
                    return itemValidation;
                }

                try
                {
                    var previews = new List<ApiInvoiceDocumentPackagePreviewItemResponse>();
                    foreach (var item in normalizedItems)
                    {
                        var renderResult = await reportHtmlService.RenderInvoiceReportAsync(
                                invoiceId,
                                ReportDocumentType.ExportDocument,
                                item.TemplatePath,
                                item.WithSeal,
                                cancellationToken)
                            .ConfigureAwait(false);

                        previews.Add(new ApiInvoiceDocumentPackagePreviewItemResponse(
                            item.Name,
                            renderResult.ReportType.ToString(),
                            renderResult.TemplatePath,
                            renderResult.WithSeal,
                            renderResult.Html));
                    }

                    return Results.Ok(new ApiInvoiceDocumentPackagePreviewResponse(
                        invoiceId,
                        previews,
                        InvoiceDocumentPackagePreviewStoragePolicy));
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewInvoiceDocumentPackageHtml");
        }

        private static void MapPaymentDraftReportHtmlPreviewEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/payments/draft/html-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportHtmlService reportHtmlService,
                ApiPaymentDraftReportHtmlPreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款/报销草稿预览请求体不能为空。"));
                }

                if (request.Payment == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款/报销草稿不能为空。"));
                }

                var requestedReportType = string.IsNullOrWhiteSpace(request.ReportType)
                    ? ReportDocumentType.PaymentVoucher.ToString()
                    : request.ReportType;
                if (!TryParseReportDocumentType(requestedReportType, out var reportType)
                    || reportType != ReportDocumentType.PaymentVoucher)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款/报销草稿 HTML 预览仅支持 PaymentVoucher 报表类型。"));
                }

                try
                {
                    var payment = ApiPaymentDtoFactory.ToPaymentForSave(request.Payment);
                    var result = await reportHtmlService.RenderPaymentVoucherDraftAsync(
                        payment,
                        request.TemplatePath,
                        request.WithSeal,
                        cancellationToken);

                    return Results.Ok(new ApiPaymentReportHtmlPreviewResponse(
                        result.SourceId,
                        result.ReportType.ToString(),
                        result.TemplatePath,
                        result.WithSeal,
                        result.Html,
                        PaymentDraftPreviewStoragePolicy));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewPaymentVoucherDraftHtml");
        }

        private static void MapPaymentReportHtmlPreviewEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/payments/{paymentId:int}/html-preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IReportHtmlService reportHtmlService,
                int paymentId,
                ApiReportHtmlPreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (paymentId <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款/报销单 ID 必须大于0。"));
                }

                request ??= new ApiReportHtmlPreviewRequest { ReportType = ReportDocumentType.PaymentVoucher.ToString() };
                if (!TryParseReportDocumentType(request.ReportType, out var reportType)
                    || reportType != ReportDocumentType.PaymentVoucher)
                {
                    return Results.BadRequest(new ApiErrorResponse("付款/报销单 HTML 预览仅支持 PaymentVoucher 报表类型。"));
                }

                try
                {
                    var result = await reportHtmlService.RenderPaymentVoucherAsync(
                        paymentId,
                        request.TemplatePath,
                        request.WithSeal,
                        cancellationToken);

                    return Results.Ok(new ApiPaymentReportHtmlPreviewResponse(
                        result.SourceId,
                        result.ReportType.ToString(),
                        result.TemplatePath,
                        result.WithSeal,
                        result.Html));
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewPaymentVoucherHtml");
        }
    }
}

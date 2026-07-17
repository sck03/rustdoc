using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;
using ExportDocManager.Services.Infrastructure;
using System.Text.Json;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string LetterOfCreditReviewStoragePolicy =
            "信用证 AI 合规审查只使用当前请求中的发票/信用证草稿字段和程序根 appsettings.json 中的 AI 配置；结果只在响应和页面状态中返回，不写数据库、不生成文件、不创建默认输出目录、不读取同号另一口径发票，也不读取付款/报销单据或系统 C 盘落点。";

        private static void MapLetterOfCreditToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/letter-of-credit/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ILetterOfCreditDocumentService documentService,
                ApiLetterOfCreditImportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string filePath = request?.FilePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("信用证文件路径不能为空。"));
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(filePath);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return Results.BadRequest(new ApiErrorResponse($"信用证文件路径无效：{ex.Message}"));
                }

                try
                {
                    var result = await documentService.ImportAsync(fullPath, cancellationToken);
                    return Results.Ok(new ApiLetterOfCreditImportResponse(
                        result.SourcePath,
                        result.SourceDescription,
                        result.ExtractedText,
                        "信用证导入只读取用户显式选择或输入的文件路径，提取文本随响应返回；不会创建系统 C 盘默认落点。OCR 模型仍随程序放在 OcrModels/ 下，sidecar 未启用 OCR 运行时时扫描件会返回明确错误。"));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
                catch (NotSupportedException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
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
            .WithName("ImportLetterOfCreditDocument");

            endpoints.MapPost("/api/tools/letter-of-credit/review", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ISettingsService settingsService,
                ILetterOfCreditComplianceReviewService reviewService,
                ApiLetterOfCreditReviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request?.Invoice == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("信用证审查发票草稿不能为空。"));
                }

                var draft = ToLetterOfCreditReviewDraft(request.Invoice);
                if (!reviewService.HasReviewContext(draft))
                {
                    return Results.BadRequest(new ApiErrorResponse("请先导入信用证文本，或至少补充信用证号/信用证要求后再进行审查。"));
                }

                await settingsService.LoadAsync();

                try
                {
                    var result = await reviewService.ReviewAsync(draft, cancellationToken);
                    return Results.Ok(new ApiLetterOfCreditReviewResponse(
                        result.ReportText,
                        result.ContextSummary,
                        result.LetterOfCreditContentTruncated,
                        LetterOfCreditReviewStoragePolicy));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (HttpRequestException ex)
                {
                    return WriteConflict($"AI 审查请求失败：{ex.Message}");
                }
                catch (JsonException ex)
                {
                    return WriteConflict($"AI 审查响应解析失败：{ex.Message}");
                }
            })
            .WithName("ReviewLetterOfCreditCompliance");
        }

        private static LetterOfCreditComplianceReviewDraft ToLetterOfCreditReviewDraft(ApiInvoiceDetailDto invoice)
        {
            return new LetterOfCreditComplianceReviewDraft
            {
                InvoiceNo = invoice.InvoiceNo,
                ContractNo = invoice.ContractNo,
                InvoiceType = invoice.Type,
                LetterOfCreditNo = invoice.LetterOfCreditNo,
                LetterOfCreditSourcePath = invoice.LetterOfCreditSourcePath,
                LetterOfCreditContent = invoice.LetterOfCreditContent,
                IssuingBank = invoice.IssuingBank,
                TotalAmount = invoice.TotalAmount,
                Currency = invoice.Currency,
                PortOfLoading = invoice.PortOfLoading,
                PortOfDestination = invoice.PortOfDestination,
                PaymentTerms = invoice.PaymentTerms,
                TradeTerms = invoice.TradeTerms,
                TransportMode = invoice.TransportMode,
                SpecialTerms = invoice.SpecialTerms
            };
        }
    }
}

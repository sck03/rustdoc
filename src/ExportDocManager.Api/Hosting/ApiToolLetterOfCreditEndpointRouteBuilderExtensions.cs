using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;
using System.Text.Json;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string LetterOfCreditReviewStoragePolicy =
            "信用证 AI 合规审查只使用当前请求中的发票/信用证草稿字段和运行数据根 Config/appsettings.json 中的 AI 配置；结果只在响应和页面状态中返回，不写数据库、不生成文件、不创建默认输出目录、不读取同号另一口径发票，也不读取付款/报销单据或系统 C 盘落点。";

        private static void MapLetterOfCreditToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/letter-of-credit/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ILetterOfCreditDocumentService documentService,
                ApiLetterOfCreditImportRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("导入本机信用证文件仅支持桌面版；浏览器版请上传文件。");
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
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("ImportLetterOfCreditDocument")
            .Produces<ApiLetterOfCreditImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/tools/letter-of-credit/import-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ILetterOfCreditDocumentService documentService,
                IAppPathProvider pathProvider,
                string? fileName,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string uploadRoot = Path.Combine(
                    pathProvider.CacheRoot,
                    "BrowserUploads",
                    "LetterOfCredit",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uploadRoot);

                try
                {
                    string safeFileName = NormalizeUploadedLetterOfCreditFileName(fileName ?? string.Empty);
                    string uploadPath = Path.Combine(uploadRoot, safeFileName);
                    await using (var output = File.Create(uploadPath))
                    {
                        await ApiUploadLimits.CopyRequestBodyAsync(
                            context.Request,
                            output,
                            ApiUploadLimits.LetterOfCreditBytes,
                            cancellationToken);
                    }

                    if (new FileInfo(uploadPath).Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("上传的信用证文件不能为空。"));
                    }

                    var result = await documentService.ImportAsync(uploadPath, cancellationToken);
                    return Results.Ok(new ApiLetterOfCreditImportResponse(
                        safeFileName,
                        result.SourceDescription,
                        result.ExtractedText,
                        "浏览器上传文件仅暂存在运行数据根 Cache/BrowserUploads/LetterOfCredit，请求结束后立即删除；响应和发票草稿只保留安全原文件名，不返回或保存服务器临时绝对路径。"));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
                }
                catch (NotSupportedException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(uploadRoot);
                }
            })
            .Accepts<IFormFile>("application/octet-stream")
            .WithName("UploadLetterOfCreditDocument")
            .Produces<ApiLetterOfCreditImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status413PayloadTooLarge);

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
                    return WriteServiceException(ex);
                }
                catch (HttpRequestException ex)
                {
                    return WriteInfrastructureFailure("AI 审查服务暂时不可用，请稍后重试。", ex);
                }
                catch (JsonException ex)
                {
                    return WriteInfrastructureFailure("AI 审查服务返回了无效响应，请稍后重试。", ex);
                }
            })
            .WithName("ReviewLetterOfCreditCompliance")
            .Produces<ApiLetterOfCreditReviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);
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

        private static string NormalizeUploadedLetterOfCreditFileName(string fileName)
        {
            string portableName = (fileName ?? string.Empty).Trim().Replace('\\', '/');
            string normalized = Path.GetFileName(portableName).Trim();
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, ".", StringComparison.Ordinal) ||
                string.Equals(normalized, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException("请上传有效的信用证文件。", nameof(fileName));
            }

            if (normalized.Length > 240 ||
                normalized.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            {
                throw new ArgumentException("信用证文件名包含无效字符或超过长度限制。", nameof(fileName));
            }

            return normalized;
        }
    }
}

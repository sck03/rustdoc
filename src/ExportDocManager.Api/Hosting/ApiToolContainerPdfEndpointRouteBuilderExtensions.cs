using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapContainerPackingPdfEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tools/container-packing/pdf/download", async (
            HttpContext context,
            IHtmlToPdfService pdfService,
            IAppPathProvider paths,
            IBusinessClock clock,
            ApiContainerPackingPdfRequest request,
            CancellationToken cancellationToken) =>
        {
            if (ValidateContainerPackingPdfRequest(request) is { } validation)
            {
                return validation;
            }

            DateTimeOffset generatedAt = clock.Now;
            string fileName = BuildContainerPackingPdfFileName(request.ProjectName, generatedAt);
            string outputPath = CreateBrowserDownloadPath(paths, "ContainerPackingPdf", fileName);
            string outputDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            bool cleanupRegistered = false;
            try
            {
                await RenderContainerPackingPdfAsync(pdfService, request, outputPath, generatedAt, cancellationToken);
                IResult response = StreamTemporaryFile(context, outputPath, "application/pdf", fileName, outputDirectory);
                cleanupRegistered = true;
                return response;
            }
            catch (ServiceException ex)
            {
                return WriteServiceException(ex);
            }
            finally
            {
                if (!cleanupRegistered)
                {
                    AtomicFileHelper.TryDeleteDirectory(outputDirectory);
                }
            }
        })
        .WithName("DownloadContainerPackingPdf")
        .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf")
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPost("/api/tools/container-packing/pdf/save-to-path", async (
            HttpContext context,
            ApiDesktopAccessOptions desktopAccessOptions,
            IHtmlToPdfService pdfService,
            IBusinessClock clock,
            ApiContainerPackingPdfRequest request,
            CancellationToken cancellationToken) =>
        {
            if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
            {
                return WriteForbidden("保存装柜 PDF 到本机路径仅支持桌面版。");
            }
            if (ValidateContainerPackingPdfRequest(request) is { } validation)
            {
                return validation;
            }

            string destinationPath;
            try
            {
                string output = request.DestinationPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(output) ||
                    !string.Equals(Path.GetExtension(output), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new ApiErrorResponse("PDF 输出路径必须是有效的 .pdf 文件。"));
                }
                destinationPath = Path.GetFullPath(output);
                DateTimeOffset generatedAt = clock.Now;
                await RenderContainerPackingPdfAsync(pdfService, request, destinationPath, generatedAt, cancellationToken);
                return Results.Ok(new ApiContainerPackingPdfSaveResponse(
                    true,
                    destinationPath,
                    new FileInfo(destinationPath).Length,
                    "装柜现场 PDF 已保存。"));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            catch (ServiceException ex)
            {
                return WriteServiceException(ex);
            }
        })
        .WithName("SaveContainerPackingPdfToPath")
        .Produces<ApiContainerPackingPdfSaveResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult? ValidateContainerPackingPdfRequest(ApiContainerPackingPdfRequest? request)
    {
        if (request?.Analysis == null || request.Container == null)
        {
            return Results.BadRequest(new ApiErrorResponse("请先完成装箱分析再导出 PDF。"));
        }
        if (request.Container.Length <= 0 || request.Container.Width <= 0 || request.Container.Height <= 0)
        {
            return Results.BadRequest(new ApiErrorResponse("装柜 PDF 的柜体尺寸无效。"));
        }
        if (request.Analysis.PackedItems is not { Count: > 0 and <= 20_000 })
        {
            return Results.BadRequest(new ApiErrorResponse("装柜 PDF 必须包含 1 至 20000 个有效装载块。"));
        }
        return null;
    }

    private static async Task RenderContainerPackingPdfAsync(
        IHtmlToPdfService pdfService,
        ApiContainerPackingPdfRequest request,
        string destinationPath,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        string html = ApiContainerPackingPdfHtmlBuilder.Build(request, generatedAt);
        await pdfService.RenderAsync(
            html,
            destinationPath,
            new HtmlToPdfRenderOptions { DocumentTitle = $"装柜方案 - {request.ProjectName}" },
            cancellationToken);
    }

    private static string BuildContainerPackingPdfFileName(string? projectName, DateTimeOffset generatedAt)
    {
        string safeName = CrossPlatformFileNamePolicy.SanitizeFileNamePart(projectName, '-', "未命名方案");
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "未命名方案";
        }
        return $"装柜方案-{safeName[..Math.Min(safeName.Length, 60)]}-{generatedAt:yyyyMMdd-HHmm}.pdf";
    }
}

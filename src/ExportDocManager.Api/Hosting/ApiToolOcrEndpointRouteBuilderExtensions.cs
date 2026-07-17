using ExportDocManager.Services.Security;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static readonly HashSet<string> SupportedOcrImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".tif",
            ".tiff"
        };

        private const int MaxOcrImageContentBytes = 25 * 1024 * 1024;

        private static void MapOcrToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/ocr/recognize-image", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IOcrService ocrService,
                ApiOcrRecognizeImageRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string filePath = request?.FilePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 图片路径不能为空。"));
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(filePath);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return Results.BadRequest(new ApiErrorResponse($"OCR 图片路径无效：{ex.Message}"));
                }

                string extension = Path.GetExtension(fullPath);
                if (!SupportedOcrImageExtensions.Contains(extension))
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 仅支持 PNG、JPG、BMP、TIFF 图片。"));
                }

                if (!File.Exists(fullPath))
                {
                    return Results.NotFound(new ApiErrorResponse("OCR 图片不存在。"));
                }

                try
                {
                    await using var stream = File.OpenRead(fullPath);
                    var result = await ocrService.RecognizeAsync(stream);
                    cancellationToken.ThrowIfCancellationRequested();

                    return Results.Ok(ApiOcrDtoFactory.FromResult(
                        result,
                        fullPath,
                        ApiOcrDtoFactory.FilePathStoragePolicy));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (NotSupportedException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("RecognizeOcrImage");

            endpoints.MapPost("/api/tools/ocr/recognize-image-content", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IOcrService ocrService,
                ApiOcrRecognizeImageContentRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string imageContentBase64 = request?.ImageContentBase64?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageContentBase64))
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 图片内容不能为空。"));
                }

                string sourceMimeType = request?.SourceMimeType?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sourceMimeType) &&
                    !sourceMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 内存图片必须使用 image/* MIME 类型。"));
                }

                int dataUrlSeparatorIndex = imageContentBase64.IndexOf(',');
                if (imageContentBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    dataUrlSeparatorIndex >= 0)
                {
                    imageContentBase64 = imageContentBase64[(dataUrlSeparatorIndex + 1)..].Trim();
                }

                byte[] imageBytes;
                try
                {
                    imageBytes = Convert.FromBase64String(imageContentBase64);
                }
                catch (FormatException)
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 图片内容不是有效的 Base64。"));
                }

                if (imageBytes.Length == 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 图片内容不能为空。"));
                }

                if (imageBytes.Length > MaxOcrImageContentBytes)
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 图片内容超过 25 MB 限制。"));
                }

                string sourceName = request?.SourceName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    sourceName = "剪贴板图片（内存）";
                }

                try
                {
                    await using var stream = new MemoryStream(imageBytes, writable: false);
                    var result = await ocrService.RecognizeAsync(stream);
                    cancellationToken.ThrowIfCancellationRequested();

                    return Results.Ok(ApiOcrDtoFactory.FromResult(
                        result,
                        sourceName,
                        ApiOcrDtoFactory.InMemoryStoragePolicy));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (NotSupportedException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("RecognizeOcrImageContent");
        }
    }
}

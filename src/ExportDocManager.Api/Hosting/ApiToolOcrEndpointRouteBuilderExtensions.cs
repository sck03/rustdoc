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

        private const int MaxOcrImageContentBytes = OcrInputLimits.MaximumImageBytes;
        private const long MaxOcrImageFileBytes = MaxOcrImageContentBytes;

        private static void MapOcrToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/ocr/recognize-image", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                IOcrService ocrService,
                ApiOcrRecognizeImageRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("识别本机图片仅支持桌面版；浏览器版请上传图片。");
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
                    if (stream.Length > MaxOcrImageFileBytes)
                    {
                        return WritePayloadTooLarge(MaxOcrImageFileBytes);
                    }

                    var result = await ocrService.RecognizeAsync(stream, cancellationToken);

                    return Results.Ok(ApiOcrDtoFactory.FromResult(
                        result,
                        fullPath,
                        ApiOcrDtoFactory.FilePathStoragePolicy));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse("OCR 图片不存在或已被移动。"));
                }
                catch (DirectoryNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse("OCR 图片目录不存在或已被移动。"));
                }
                catch (UnauthorizedAccessException)
                {
                    return WriteForbidden("没有权限读取所选 OCR 图片。请检查文件权限或重新选择图片。");
                }
                catch (IOException ex)
                {
                    return WriteServiceException(ex);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
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

                // Reject oversized encoded payloads before allocating the decoded
                // byte array.  Base64 expands the binary payload by roughly 4/3.
                int maxEncodedLength = ((MaxOcrImageContentBytes + 2) / 3) * 4;
                if (imageContentBase64.Length > maxEncodedLength)
                {
                    return WritePayloadTooLarge(MaxOcrImageContentBytes);
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
                    return WritePayloadTooLarge(MaxOcrImageContentBytes);
                }

                string sourceName = request?.SourceName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    sourceName = "剪贴板图片（内存）";
                }

                try
                {
                    await using var stream = new MemoryStream(imageBytes, writable: false);
                    var result = await ocrService.RecognizeAsync(stream, cancellationToken);

                    return Results.Ok(ApiOcrDtoFactory.FromResult(
                        result,
                        sourceName,
                        ApiOcrDtoFactory.InMemoryStoragePolicy));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
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

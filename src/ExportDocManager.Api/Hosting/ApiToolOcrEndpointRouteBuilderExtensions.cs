using System.Collections.Frozen;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Tools;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static readonly FrozenSet<string> SupportedOcrImageExtensions = new[]
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".tif",
            ".tiff"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private const int MaxOcrImageContentBytes = OcrInputLimits.MaximumImageBytes;
        private const long MaxOcrImageFileBytes = MaxOcrImageContentBytes;

        private static void MapOcrToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/ocr/recognize-image", async (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                IOcrService ocrService,
                ApiOcrRecognizeImageRequest request,
                CancellationToken cancellationToken) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("识别本机图片仅支持桌面版；浏览器版请上传图片。");
                }

                if (!TryResolveOcrImagePath(request?.FilePath, out string fullPath, out IResult? error))
                {
                    return error!;
                }

                try
                {
                    await using var stream = File.OpenRead(fullPath);
                    if (stream.Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("OCR 图片不能为空。"));
                    }
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
            .WithName("RecognizeOcrImage")
            .Produces<ApiOcrRecognizeImageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapGet("/api/tools/ocr/preview-image", (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                string? filePath) =>
            {

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("预览本机图片仅支持桌面版。");
                }

                if (!TryResolveOcrImagePath(filePath, out string fullPath, out IResult? error))
                {
                    return error!;
                }

                try
                {
                    var file = new FileInfo(fullPath);
                    if (file.Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("OCR 图片不能为空。"));
                    }
                    if (file.Length > MaxOcrImageFileBytes)
                    {
                        return WritePayloadTooLarge(MaxOcrImageFileBytes);
                    }

                    return Results.File(fullPath, GetOcrImageMimeType(fullPath));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse("OCR 图片不存在或已被移动。"));
                }
                catch (UnauthorizedAccessException)
                {
                    return WriteForbidden("没有权限读取所选 OCR 图片。请检查文件权限或重新选择图片。");
                }
                catch (IOException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("PreviewOcrImage")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status413PayloadTooLarge);

            endpoints.MapPost("/api/tools/ocr/recognize-image-upload", async (
                HttpContext context,
                IOcrService ocrService,
                string? sourceName,
                string? sourceMimeType,
                CancellationToken cancellationToken) =>
            {

                string normalizedMimeType = sourceMimeType?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(normalizedMimeType) && !IsSupportedOcrMimeType(normalizedMimeType))
                {
                    return Results.BadRequest(new ApiErrorResponse("OCR 上传仅支持 PNG、JPG、BMP、TIFF 图片。"));
                }

                string normalizedSourceName = sourceName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(normalizedSourceName))
                {
                    normalizedSourceName = "剪贴板图片（内存）";
                }

                try
                {
                    await using var stream = new MemoryStream();
                    await ApiUploadLimits.CopyRequestBodyAsync(
                        context.Request,
                        stream,
                        MaxOcrImageContentBytes,
                        cancellationToken);
                    if (stream.Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("OCR 图片内容不能为空。"));
                    }
                    stream.Position = 0;
                    var result = await ocrService.RecognizeAsync(stream, cancellationToken);

                    return Results.Ok(ApiOcrDtoFactory.FromResult(
                        result,
                        normalizedSourceName,
                        ApiOcrDtoFactory.UploadStoragePolicy));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
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
            .Accepts<byte[]>("application/octet-stream")
            .WithName("UploadOcrImage")
            .Produces<ApiOcrRecognizeImageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        }

        private static bool TryResolveOcrImagePath(string? filePath, out string fullPath, out IResult? error)
        {
            fullPath = string.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = Results.BadRequest(new ApiErrorResponse("OCR 图片路径不能为空。"));
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(filePath.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                error = Results.BadRequest(new ApiErrorResponse($"OCR 图片路径无效：{ex.Message}"));
                return false;
            }

            if (!SupportedOcrImageExtensions.Contains(Path.GetExtension(fullPath)))
            {
                error = Results.BadRequest(new ApiErrorResponse("OCR 仅支持 PNG、JPG、BMP、TIFF 图片。"));
                return false;
            }
            if (!File.Exists(fullPath))
            {
                error = Results.NotFound(new ApiErrorResponse("OCR 图片不存在。"));
                return false;
            }
            return true;
        }

        private static bool IsSupportedOcrMimeType(string mimeType) =>
            mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("image/tiff", StringComparison.OrdinalIgnoreCase);

        private static string GetOcrImageMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }
}

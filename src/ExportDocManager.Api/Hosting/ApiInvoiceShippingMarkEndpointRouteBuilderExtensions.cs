using ExportDocManager.Services.Core;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceShippingMarkEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/invoices/shipping-marks/image", async (
                IShippingMarkImageService imageService,
                ApiShippingMarkImageSaveRequest request,
                CancellationToken cancellationToken) =>
            {

                if (request == null || string.IsNullOrWhiteSpace(request.ImageDataUrl))
                {
                    return Results.BadRequest(new ApiErrorResponse("唛头图片内容不能为空。"));
                }

                try
                {
                    var result = await imageService.SavePngDataUrlAsync(request.ImageDataUrl, cancellationToken);
                    return Results.Ok(new ApiShippingMarkImageSaveResponse(
                        result.ImagePath,
                        result.FileName,
                        result.ContentType,
                        result.SizeBytes,
                        result.StoragePolicy));
                }
                catch (FormatException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("SaveShippingMarkImage")
            .Produces<ApiShippingMarkImageSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/invoices/shipping-marks/image/preview", async (
                IShippingMarkImageService imageService,
                ApiShippingMarkImagePreviewRequest request,
                CancellationToken cancellationToken) =>
            {

                if (request == null || string.IsNullOrWhiteSpace(request.ImagePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("唛头图片路径不能为空。"));
                }

                try
                {
                    var result = await imageService.ReadImageAsDataUrlAsync(request.ImagePath, cancellationToken);
                    return Results.Ok(new ApiShippingMarkImagePreviewResponse(
                        result.ImagePath,
                        result.FileName,
                        result.ContentType,
                        result.SizeBytes,
                        result.DataUrl,
                        result.StoragePolicy));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex) { return WriteServiceException(ex); }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("PreviewShippingMarkImage")
            .Produces<ApiShippingMarkImagePreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

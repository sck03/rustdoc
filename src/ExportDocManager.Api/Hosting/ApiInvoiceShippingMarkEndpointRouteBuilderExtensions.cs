using ExportDocManager.Services.Core;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceShippingMarkEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/invoices/shipping-marks/image", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IShippingMarkImageService imageService,
                ApiShippingMarkImageSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
            .WithName("SaveShippingMarkImage");

            endpoints.MapPost("/api/invoices/shipping-marks/image/preview", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IShippingMarkImageService imageService,
                ApiShippingMarkImagePreviewRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
                catch (FormatException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (FileNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("PreviewShippingMarkImage");
        }
    }
}

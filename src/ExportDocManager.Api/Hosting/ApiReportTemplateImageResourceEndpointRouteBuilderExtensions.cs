using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapReportTemplateImageResourceEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/reports/templates/v3/resources/upload", async (
            HttpContext context,
            IReportTemplateImageResourceAccessService resourceAccessService,
            IReportTemplateImageResourceService resourceService,
            string? fileName,
            string? mediaType,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.ContentLength is > ReportTemplateV3ContractCatalog.MaxResourceBytes)
            {
                return WritePayloadTooLarge(ReportTemplateV3ContractCatalog.MaxResourceBytes);
            }

            try
            {
                var resource = await resourceService.StoreAndCommitAsync(
                    context.Request.Body,
                    fileName,
                    mediaType,
                    resourceAccessService.RegisterUploadAsync,
                    cancellationToken);
                return Results.Ok(ToApiResource(resource));
            }
            catch (PayloadLimitExceededException ex) { return WritePayloadTooLarge(ex); }
            catch (Exception ex) when (ex is ServiceException or ArgumentException or InvalidDataException)
            {
                return WriteServiceException(ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return WriteServiceException(ex);
            }
        })
        .Accepts<IFormFile>("application/octet-stream")
        .WithName("UploadReportTemplateV3ImageResource")
        .WithApiCapability(PermissionResourceCatalog.ReportResources, PermissionAction.Upload)
        .Produces<ApiReportTemplateImageResourceResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status413PayloadTooLarge)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet("/api/reports/templates/v3/resources/{resourceId}", async (
            IReportTemplateImageResourceAccessService resourceAccessService,
            string resourceId,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var resource = await resourceAccessService.ReadAsync(resourceId, cancellationToken);
                return Results.File(
                    resource.Content,
                    resource.Resource.MediaType,
                    enableRangeProcessing: false);
            }
            catch (Exception ex) when (ex is ServiceException or ArgumentException or InvalidDataException)
            {
                return WriteServiceException(ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return WriteServiceException(ex);
            }
        })
        .WithName("DownloadReportTemplateV3ImageResource")
        .WithApiCapability(PermissionResourceCatalog.ReportResources, PermissionAction.View)
        .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapDelete("/api/reports/templates/v3/resources/{resourceId}", async (
            IReportTemplateImageResourceAccessService resourceAccessService,
            string resourceId,
            CancellationToken cancellationToken) =>
        {
            try
            {
                bool deletePhysicalFile = await resourceAccessService.RecycleAsync(resourceId, cancellationToken);
                return Results.Ok(new ApiCommandResponse(
                    true,
                    deletePhysicalFile
                        ? "图片资源已安全回收。"
                        : "当前用户的上传归属已解除；资源仍被其他上传者持有。"));
            }
            catch (Exception ex) when (ex is ServiceException or ArgumentException or InvalidDataException)
            {
                return WriteServiceException(ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return WriteServiceException(ex);
            }
        })
        .WithName("RecycleReportTemplateV3ImageResource")
        .WithApiCapability(PermissionResourceCatalog.ReportResources, PermissionAction.Recycle)
        .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static ApiReportTemplateImageResourceResponse ToApiResource(ReportTemplateImageResource resource) =>
        new(
            resource.Id,
            resource.MediaType,
            resource.ByteLength,
            resource.Sha256,
            resource.AltText,
            resource.StoragePolicy);
}

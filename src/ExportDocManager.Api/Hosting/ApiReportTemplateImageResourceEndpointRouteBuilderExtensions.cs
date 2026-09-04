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
            ApiAuthorizationService authorizationService,
            IReportTemplateImageResourceService resourceService,
            string? fileName,
            string? mediaType,
            CancellationToken cancellationToken) =>
        {
            var user = ApiEndpointAuth.GetRequiredUser(context);
            if (!authorizationService.CanUseModule(
                    user,
                    PermissionModuleCatalog.DocumentReports,
                    PermissionAccessLevel.Manage))
            {
                return WriteForbidden("当前权限模板不允许上传受控图片资源。");
            }

            if (context.Request.ContentLength is > ReportTemplateV3ContractCatalog.MaxResourceBytes)
            {
                return WritePayloadTooLarge(ReportTemplateV3ContractCatalog.MaxResourceBytes);
            }

            try
            {
                var resource = await resourceService.StoreAsync(
                    context.Request.Body,
                    fileName,
                    mediaType,
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
        .Produces<ApiReportTemplateImageResourceResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status413PayloadTooLarge)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet("/api/reports/templates/v3/resources/{resourceId}", async (
            IReportTemplateImageResourceService resourceService,
            string resourceId,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var resource = await resourceService.ReadAsync(resourceId, cancellationToken);
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
        .Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream")
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
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

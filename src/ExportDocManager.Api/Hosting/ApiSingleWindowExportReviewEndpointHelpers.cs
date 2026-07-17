using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static async Task<IResult> BuildSingleWindowExportReviewAsync(
            ISingleWindowExportReviewService exportReviewService,
            ISettingsService settingsService,
            SingleWindowBusinessType businessType,
            int invoiceId,
            CancellationToken cancellationToken)
        {
            try
            {
                await settingsService.LoadAsync();
                var review = await exportReviewService.BuildSubmitReviewAsync(
                    businessType,
                    invoiceId,
                    cancellationToken);
                return Results.Ok(review);
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }
    }
}

using ExportDocManager.Services.Core;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceDataMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/system/data-maintenance/invoices/{id:int}", async Task<Results<
                Ok<ApiInvoiceDataMaintenancePreviewResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound>>(
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IInvoiceDataMaintenanceService maintenanceService,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return TypedForbidden("只有管理员可以使用发票数据维护功能。");
                }

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("发票 ID 必须大于 0。"));
                }

                var preview = await maintenanceService
                    .GetPurgePreviewAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                return preview == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(new ApiInvoiceDataMaintenancePreviewResponse(
                        preview.Id,
                        preview.InvoiceNo,
                        preview.Type,
                        preview.Status,
                        preview.StatusDisplayName,
                        preview.InvoiceDate,
                        preview.CustomerName,
                        preview.CanPurge,
                        preview.Guidance,
                        preview.StoragePolicy));
            })
            .WithName("GetInvoiceDataMaintenancePreview")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/system/data-maintenance/invoices/{id:int}/purge", async Task<Results<
                Ok<ApiInvoicePurgeResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound,
                Conflict<ApiErrorResponse>>>(
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IInvoiceDataMaintenanceService maintenanceService,
                int id,
                ApiInvoicePurgeRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return TypedForbidden("只有管理员可以使用发票数据维护功能。");
                }

                if (id <= 0 || request is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("发票 ID 和数据清理请求不能为空。"));
                }

                try
                {
                    var result = await maintenanceService
                        .PurgeCancelledInvoiceAsync(
                            new InvoicePurgeCommand(
                                id,
                                request.InvoiceNoConfirmation,
                                request.Reason),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return result == null
                        ? TypedResults.NotFound()
                        : TypedResults.Ok(new ApiInvoicePurgeResponse(
                            result.Success,
                            result.InvoiceId,
                            result.InvoiceNo,
                            result.PreviousStatus,
                            result.Message,
                            result.StoragePolicy));
                }
                catch (InvoiceValidationException ex)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvoiceConflictException ex)
                {
                    return TypedResults.Conflict(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return TypedForbidden(ex.Message);
                }
            })
            .WithName("PurgeCancelledInvoice")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);
        }
    }
}

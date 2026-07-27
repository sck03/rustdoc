using ExportDocManager.Services.Core;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapInvoiceDataMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/system/data-maintenance/invoices/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IInvoiceDataMaintenanceService maintenanceService,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以使用发票数据维护功能。");
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票 ID 必须大于 0。"));
                }

                var preview = await maintenanceService
                    .GetPurgePreviewAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                return preview == null
                    ? Results.NotFound()
                    : Results.Ok(new ApiInvoiceDataMaintenancePreviewResponse(
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
            .WithName("GetInvoiceDataMaintenancePreview");

            endpoints.MapPost("/api/system/data-maintenance/invoices/{id:int}/purge", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IInvoiceDataMaintenanceService maintenanceService,
                int id,
                ApiInvoicePurgeRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以使用发票数据维护功能。");
                }

                if (id <= 0 || request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("发票 ID 和数据清理请求不能为空。"));
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
                        ? Results.NotFound()
                        : Results.Ok(new ApiInvoicePurgeResponse(
                            result.Success,
                            result.InvoiceId,
                            result.InvoiceNo,
                            result.PreviousStatus,
                            result.Message,
                            result.StoragePolicy));
                }
                catch (InvoiceValidationException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvoiceConflictException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
            })
            .WithName("PurgeCancelledInvoice");
        }
    }
}

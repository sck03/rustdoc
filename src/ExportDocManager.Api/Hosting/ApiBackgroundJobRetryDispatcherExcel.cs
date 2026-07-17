using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public sealed partial class ApiBackgroundJobRetryDispatcher
    {
        private IResult RetryExcelTemplateExportJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiExcelOutputRequest>(sourceJob, out var templateRequest, out var templateError))
            {
                return templateError;
            }

            var templateValidation = ApiEndpointRouteBuilderExtensions.ValidateExcelDestinationPath(
                templateRequest?.DestinationPath,
                "Excel 模板输出路径",
                out string templateDestinationPath);
            if (templateValidation != null)
            {
                return templateValidation;
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueExcelTemplateExportJob(
                    jobRunner,
                    requestedBy,
                    templateDestinationPath));
        }

        private IResult RetryBlankBookingSheetExportJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiExcelOutputRequest>(sourceJob, out var blankRequest, out var blankError))
            {
                return blankError;
            }

            var blankValidation = ApiEndpointRouteBuilderExtensions.ValidateExcelDestinationPath(
                blankRequest?.DestinationPath,
                "空白托单输出路径",
                out string blankDestinationPath);
            if (blankValidation != null)
            {
                return blankValidation;
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueBlankBookingSheetExportJob(
                    jobRunner,
                    requestedBy,
                    blankDestinationPath));
        }

        private IResult RetryBookingSheetConvertJob(
            BackgroundJobSnapshot sourceJob,
            string requestedBy)
        {
            if (!TryDeserializeRetryRequest<ApiExcelConvertBookingSheetRequest>(sourceJob, out var convertRequest, out var convertError))
            {
                return convertError;
            }

            var sourceValidation = ApiEndpointRouteBuilderExtensions.ValidateExcelSourcePath(
                convertRequest?.SourcePath,
                "导入模板源文件",
                out string convertSourcePath);
            if (sourceValidation != null)
            {
                return sourceValidation;
            }

            var convertDestinationValidation = ApiEndpointRouteBuilderExtensions.ValidateExcelDestinationPath(
                convertRequest?.DestinationPath,
                "订舱托单输出路径",
                out string convertDestinationPath);
            if (convertDestinationValidation != null)
            {
                return convertDestinationValidation;
            }

            if (string.Equals(convertSourcePath, convertDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("订舱托单必须另存为新文件，不能覆盖源 Excel。"));
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueBookingSheetConvertJob(
                    jobRunner,
                    requestedBy,
                    convertSourcePath,
                    convertDestinationPath));
        }

        private async Task<IResult> RetryInvoiceBookingSheetExportJobAsync(
            BackgroundJobSnapshot sourceJob,
            string requestedBy,
            IInvoiceService invoiceService)
        {
            if (!TryDeserializeRetryRequest<ApiInvoiceBookingSheetRequest>(sourceJob, out var invoiceBookingRequest, out var invoiceBookingError))
            {
                return invoiceBookingError;
            }

            if (invoiceBookingRequest == null || invoiceBookingRequest.InvoiceId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
            }

            var invoiceBookingDestinationValidation = ApiEndpointRouteBuilderExtensions.ValidateExcelDestinationPath(
                invoiceBookingRequest.DestinationPath,
                "订舱托单输出路径",
                out string invoiceBookingDestinationPath);
            if (invoiceBookingDestinationValidation != null)
            {
                return invoiceBookingDestinationValidation;
            }

            var invoice = await invoiceService.GetInvoiceByIdAsync(invoiceBookingRequest.InvoiceId);
            if (invoice == null)
            {
                return Results.NotFound(new ApiErrorResponse("未找到指定的发票。"));
            }

            return ApiEndpointRouteBuilderExtensions.AcceptedBackgroundJob(
                ApiEndpointRouteBuilderExtensions.EnqueueInvoiceBookingSheetExportJob(
                    jobRunner,
                    requestedBy,
                    invoice,
                    invoiceBookingRequest.InvoiceId,
                    invoiceBookingDestinationPath));
        }
    }
}

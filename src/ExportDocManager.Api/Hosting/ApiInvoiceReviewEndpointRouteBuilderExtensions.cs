using ExportDocManager.Services.Core;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapInvoiceReviewEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/invoices/review", (ApiInvoiceDetailDto request) =>
        {
            if (request.Items.Count > 2000) throw new InvoiceValidationException("单张发票最多允许 2000 行商品明细。");
            return TypedResults.Ok(InvoiceReviewPolicy.Evaluate(ApiInvoiceDtoFactory.ToInvoiceForSave(request)));
        }).WithName("ReviewInvoice").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Operate);
    }
}

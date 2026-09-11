using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Opportunities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Suppliers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapCrmCustomerReadEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/crm/customers/{id:int}", async Task<Ok<ApiCrmCustomerDto>> (
            int id, ICrmService service, CancellationToken ct) =>
            TypedResults.Ok(ToApiDto(await service.GetCustomerAsync(id, ct))))
        .WithName("GetCrmCustomer")
        .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.View)
        .Produces(StatusCodes.Status404NotFound);

    private static void MapSupplierReadEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/suppliers/{id:int}", async Task<Ok<ApiSupplierDto>> (
            int id, ISupplierDirectoryService service, CancellationToken ct) =>
            TypedResults.Ok(ToApiDto(await service.GetAsync(id, ct))))
        .WithName("GetSupplier")
        .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.View)
        .Produces(StatusCodes.Status404NotFound);

    private static void MapSalesOpportunityReadEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/crm/opportunities/{id:int}", async Task<Ok<ApiSalesOpportunityDto>> (
            int id, ISalesOpportunityService service, CancellationToken ct) =>
            TypedResults.Ok(ToApiDto(await service.GetAsync(id, ct))))
        .WithName("GetSalesOpportunity")
        .WithApiCapability(PermissionResourceCatalog.SalesOpportunities, PermissionAction.View)
        .Produces(StatusCodes.Status404NotFound);
}

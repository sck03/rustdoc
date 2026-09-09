using ExportDocManager.Services.Attachments;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapBusinessAttachmentCategoryEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/business-attachment-categories", async (IServiceProvider services, int? invoiceId, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).ListCategoriesAsync(invoiceId, cancellationToken)))
            .WithName("ListBusinessAttachmentCategories").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.View);
        endpoints.MapPost("/api/business-attachment-categories", async (IServiceProvider services, BusinessAttachmentCategoryCreate request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).CreateCategoryAsync(request, cancellationToken)))
            .WithName("CreateBusinessAttachmentCategory").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Manage);
        endpoints.MapPut("/api/business-attachment-categories/{id:int:min(1)}", async (IServiceProvider services, int id, BusinessAttachmentCategoryUpdate request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await AttachmentService(services).UpdateCategoryAsync(id, request, cancellationToken)))
            .WithName("UpdateBusinessAttachmentCategory").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Manage);
        endpoints.MapDelete("/api/business-attachment-categories/{id:int:min(1)}", async (IServiceProvider services, int id, int expectedVersion, CancellationToken cancellationToken) =>
        {
            await AttachmentService(services).DeleteCategoryAsync(id, expectedVersion, cancellationToken);
            return TypedResults.Ok(new ApiCommandResponse(true, "资料分类已删除。"));
        }).WithName("DeleteBusinessAttachmentCategory").WithApiCapability(PermissionModuleCatalog.DocumentInvoices, PermissionAction.Manage);
    }
}

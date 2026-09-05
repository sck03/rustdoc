using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapOrganizationDirectoryEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/organization-directory", async (
                IOrganizationDirectoryService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var directory = await service.ListAsync(cancellationToken);
                    return Results.Ok(new ApiOrganizationDirectoryResponse(
                        directory.Companies.Select(ToOrganizationCompanyDto).ToArray(),
                        directory.Departments.Select(ToOrganizationDepartmentDto).ToArray()));
                }
                catch (ServiceException exception)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("GetOrganizationDirectory")
            .WithApiCapability(PermissionResourceCatalog.SystemUsers, PermissionAction.Manage)
            .Produces<ApiOrganizationDirectoryResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/organization-directory/companies", async (
                IOrganizationDirectoryService service,
                ApiOrganizationCompanySaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request == null || request.ExpectedVersion != 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增公司不能包含已有版本号。"));
                }
                try
                {
                    var saved = await service.SaveCompanyAsync(
                        new OrganizationCompanySaveRequest(
                            string.Empty, request.Code, request.Name, request.IsActive, 0),
                        cancellationToken);
                    return Results.Created(
                        $"/api/organization-directory/companies/{Uri.EscapeDataString(saved.Code)}",
                        ToOrganizationCompanyDto(saved));
                }
                catch (ServiceException exception)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("CreateOrganizationCompany")
            .WithApiCapability(PermissionResourceCatalog.SystemUsers, PermissionAction.Manage)
            .Produces<ApiOrganizationCompanyDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/organization-directory/companies/{code}", async (
                IOrganizationDirectoryService service,
                string code,
                ApiOrganizationCompanySaveRequest request,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var saved = await service.SaveCompanyAsync(
                        new OrganizationCompanySaveRequest(
                            code, request?.Code ?? string.Empty, request?.Name ?? string.Empty,
                            request?.IsActive ?? false, request?.ExpectedVersion ?? 0),
                        cancellationToken);
                    return Results.Ok(ToOrganizationCompanyDto(saved));
                }
                catch (ServiceException exception)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("UpdateOrganizationCompany")
            .WithApiCapability(PermissionResourceCatalog.SystemUsers, PermissionAction.Manage)
            .Produces<ApiOrganizationCompanyDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/organization-directory/departments", async (
                IOrganizationDirectoryService service,
                ApiOrganizationDepartmentSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request == null || request.ExpectedVersion != 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增部门不能包含已有版本号。"));
                }
                try
                {
                    var saved = await service.SaveDepartmentAsync(
                        new OrganizationDepartmentSaveRequest(
                            string.Empty, request.Code, request.CompanyCode, request.Name,
                            request.IsActive, 0),
                        cancellationToken);
                    return Results.Created(
                        $"/api/organization-directory/departments/{Uri.EscapeDataString(saved.Code)}",
                        ToOrganizationDepartmentDto(saved));
                }
                catch (ServiceException exception)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("CreateOrganizationDepartment")
            .WithApiCapability(PermissionResourceCatalog.SystemUsers, PermissionAction.Manage)
            .Produces<ApiOrganizationDepartmentDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/organization-directory/departments/{code}", async (
                IOrganizationDirectoryService service,
                string code,
                ApiOrganizationDepartmentSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var saved = await service.SaveDepartmentAsync(
                        new OrganizationDepartmentSaveRequest(
                            code, request?.Code ?? string.Empty, request?.CompanyCode ?? string.Empty,
                            request?.Name ?? string.Empty, request?.IsActive ?? false,
                            request?.ExpectedVersion ?? 0),
                        cancellationToken);
                    return Results.Ok(ToOrganizationDepartmentDto(saved));
                }
                catch (ServiceException exception)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("UpdateOrganizationDepartment")
            .WithApiCapability(PermissionResourceCatalog.SystemUsers, PermissionAction.Manage)
            .Produces<ApiOrganizationDepartmentDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        }
    }
}

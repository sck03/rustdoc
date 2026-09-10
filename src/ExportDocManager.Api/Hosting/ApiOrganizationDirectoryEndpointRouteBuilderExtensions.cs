using ExportDocManager.Models;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Mvc;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapOrganizationDirectoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/organization-directory", async (IOrganizationDirectoryService service, CancellationToken token) =>
        {
            var directory = await service.ListAsync(token);
            return TypedResults.Ok(new ApiOrganizationDirectoryResponse(
                directory.Companies.Select(ToOrganizationCompanyDto).ToArray(),
                directory.Departments.Select(ToOrganizationDepartmentDto).ToArray()));
        }).OrganizationEndpoint("GetOrganizationDirectory");

        endpoints.MapGet("/api/organization-directory/managers", async (IOrganizationDirectoryService service, string companyCode,
            string? keyword, int? pageNumber, int? pageSize, CancellationToken token) =>
            TypedResults.Ok(await service.ManagerOptionsAsync(companyCode, keyword, pageNumber ?? 1, pageSize ?? 20, token)))
            .OrganizationEndpoint("ListOrganizationManagers");

        endpoints.MapPost("/api/organization-directory/companies", async (IOrganizationDirectoryService service,
            ApiOrganizationCompanySaveRequest request, CancellationToken token) =>
        {
            var saved = await service.SaveCompanyAsync(new("", request.Code, request.Name, request.IsActive, request.ExpectedVersion), token);
            return TypedResults.Created($"/api/organization-directory/companies/{Uri.EscapeDataString(saved.Code)}", ToOrganizationCompanyDto(saved));
        }).OrganizationEndpoint("CreateOrganizationCompany");

        endpoints.MapPut("/api/organization-directory/companies/{code}", async (IOrganizationDirectoryService service,
            string code, ApiOrganizationCompanySaveRequest request, CancellationToken token) =>
            TypedResults.Ok(ToOrganizationCompanyDto(await service.SaveCompanyAsync(
                new(code, request.Code, request.Name, request.IsActive, request.ExpectedVersion), token))))
            .OrganizationEndpoint("UpdateOrganizationCompany");

        endpoints.MapDelete("/api/organization-directory/companies/{code}", async (IOrganizationDirectoryService service,
            string code, [FromBody] DeleteRecordRequest request, CancellationToken token) =>
        {
            await service.DeleteCompanyAsync(code, request, token);
            return TypedResults.NoContent();
        }).OrganizationEndpoint("DeleteOrganizationCompany");

        endpoints.MapPost("/api/organization-directory/departments", async (IOrganizationDirectoryService service,
            ApiOrganizationDepartmentSaveRequest request, CancellationToken token) =>
        {
            var saved = await service.SaveDepartmentAsync(new("", request.Code, request.CompanyCode, request.Name,
                request.IsActive, request.ExpectedVersion, request.ParentCode, request.ManagerEmployeeId), token);
            return TypedResults.Created($"/api/organization-directory/departments/{Uri.EscapeDataString(saved.Code)}", ToOrganizationDepartmentDto(saved));
        }).OrganizationEndpoint("CreateOrganizationDepartment");

        endpoints.MapPut("/api/organization-directory/departments/{code}", async (IOrganizationDirectoryService service,
            string code, ApiOrganizationDepartmentSaveRequest request, CancellationToken token) =>
            TypedResults.Ok(ToOrganizationDepartmentDto(await service.SaveDepartmentAsync(new(code, request.Code, request.CompanyCode,
                request.Name, request.IsActive, request.ExpectedVersion, request.ParentCode, request.ManagerEmployeeId), token))))
            .OrganizationEndpoint("UpdateOrganizationDepartment");

        endpoints.MapDelete("/api/organization-directory/departments/{code}", async (IOrganizationDirectoryService service,
            string code, [FromBody] DeleteRecordRequest request, CancellationToken token) =>
        {
            await service.DeleteDepartmentAsync(code, request, token);
            return TypedResults.NoContent();
        }).OrganizationEndpoint("DeleteOrganizationDepartment");
    }

    private static RouteHandlerBuilder OrganizationEndpoint(this RouteHandlerBuilder endpoint, string name) =>
        endpoint.WithName(name).WithApiCapability(PermissionResourceCatalog.SystemUsers, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ApiErrorResponse>(StatusCodes.Status504GatewayTimeout);
}

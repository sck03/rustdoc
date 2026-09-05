using ExportDocManager.Services.Security;
using ExportDocManager.Services.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPermissionTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/permission-templates", async Task<Results<
                Ok<ApiPermissionTemplateCatalogResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>> (
                HttpContext context,
                IPermissionTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var templates = await service.ListAsync(cancellationToken);
                return TypedResults.Ok(new ApiPermissionTemplateCatalogResponse(
                    PermissionResourceCatalog.Resources.Select(ToApiDto).ToArray(),
                    templates.Select(ToApiDto).ToArray(),
                    PermissionDataScope.Values,
                    PermissionAccessLevel.Levels,
                    "模板修改后现有会话立即失效；服务端按资源、动作、数据范围、技术依赖和产品版本计算最终权限。"));
            })
            .WithName("ListPermissionTemplates")
            .WithApiCapability(PermissionResourceCatalog.SystemPermissions, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/permission-templates", async Task<Results<
                Ok<ApiPermissionTemplateDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPermissionTemplateService service,
                ApiPermissionTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
                await SavePermissionTemplateAsync(
                    context,
                    tokenService,
                    service,
                    request,
                    null,
                    cancellationToken))
            .WithName("CreatePermissionTemplate")
            .WithApiCapability(PermissionResourceCatalog.SystemPermissions, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/permission-templates/{id:int}", async Task<Results<
                Ok<ApiPermissionTemplateDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPermissionTemplateService service,
                int id,
                ApiPermissionTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
                await SavePermissionTemplateAsync(
                    context,
                    tokenService,
                    service,
                    request,
                    id,
                    cancellationToken))
            .WithName("UpdatePermissionTemplate")
            .WithApiCapability(PermissionResourceCatalog.SystemPermissions, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapDelete("/api/permission-templates/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPermissionTemplateService service,
                int id,
                int? expectedVersion,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    bool deleted = await service.DeleteAsync(id, cancellationToken, expectedVersion ?? 0);
                    return deleted
                        ? TypedResults.Ok(new ApiCommandResponse(true, "权限模板已删除。"))
                        : TypedResults.NotFound(new ApiErrorResponse("未找到权限模板。"));
                }
                catch (ResourceConflictException ex)
                {
                    return TypedResults.Conflict(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("DeletePermissionTemplate")
            .WithApiCapability(PermissionResourceCatalog.SystemPermissions, PermissionAction.Manage)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);
        }

        private static async Task<Results<
            Ok<ApiPermissionTemplateDto>,
            BadRequest<ApiErrorResponse>,
            UnauthorizedHttpResult,
            JsonHttpResult<ApiErrorResponse>,
            NotFound<ApiErrorResponse>,
            Conflict<ApiErrorResponse>>> SavePermissionTemplateAsync(
            HttpContext context,
            IApiSessionTokenService tokenService,
            IPermissionTemplateService service,
            ApiPermissionTemplateSaveRequest request,
            int? id,
            CancellationToken cancellationToken)
        {
            if (request is null)
            {
                return TypedResults.BadRequest(new ApiErrorResponse("权限模板请求不能为空。"));
            }

            try
            {
                var saved = await service.SaveAsync(
                    new PermissionTemplateSaveRequest(
                        id ?? request.Id,
                        request.Code,
                        request.Name,
                        request.Description,
                        request.IsActive,
                        (request.Grants ?? []).Select(grant =>
                            new PermissionGrantRecord(grant.ResourceKey, grant.Action, grant.DataScope)).ToArray(),
                        request.ExpectedVersion),
                    cancellationToken);
                var assignedUserIds = await service.ListAssignedUserIdsAsync(saved.Id, cancellationToken);
                foreach (int userId in assignedUserIds)
                {
                    await tokenService.RevokeUserSessionsAsync(userId, cancellationToken);
                }
                return TypedResults.Ok(ToApiDto(saved));
            }
            catch (ServiceValidationException ex)
            {
                return TypedResults.BadRequest(new ApiErrorResponse(ex.Message));
            }
            catch (PermissionDeniedException ex)
            {
                return TypedForbidden(ex.Message);
            }
            catch (ResourceNotFoundException ex)
            {
                return TypedResults.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (ResourceConflictException ex)
            {
                return TypedResults.Conflict(new ApiErrorResponse(ex.Message));
            }
        }

        private static ApiPermissionResourceDefinitionDto ToApiDto(PermissionResourceDefinition resource) =>
            new(resource.Key, resource.Name, resource.Group, resource.Workspace, resource.ModuleKey,
                resource.SortOrder, resource.IsTechnical, resource.SupportsDataScope,
                resource.Actions.Select(action => new ApiPermissionActionDefinitionDto(
                    action.Key, action.Name, action.Description, action.SortOrder,
                    action.NavigationAccessLevel)).ToArray());

        private static ApiPermissionTemplateDto ToApiDto(PermissionTemplateRecord template) =>
            new(
                template.Id,
                template.Code,
                template.Name,
                template.Description,
                template.IsSystem,
                template.IsActive,
                template.UpdatedAt,
                template.Grants.Select(grant =>
                    new ApiPermissionGrantDto(grant.ResourceKey, grant.Action, grant.DataScope)).ToArray(),
                template.EffectiveGrants.Select(grant => new ApiEffectivePermissionGrantDto(
                    grant.ResourceKey, grant.Action, grant.DataScope, grant.Source,
                    grant.SourceResourceKey)).ToArray(),
                template.VersionNumber);
    }
}

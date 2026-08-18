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
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IPermissionTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageUsers(user))
                {
                    return TypedForbidden("只有全功能版管理员可以管理权限模板。");
                }

                var templates = await service.ListAsync(cancellationToken);
                return TypedResults.Ok(new ApiPermissionTemplateCatalogResponse(
                    PermissionModuleCatalog.Modules.Select(ToApiDto).ToArray(),
                    templates.Select(ToApiDto).ToArray(),
                    PermissionAccessLevel.Levels,
                    "模板修改后，使用该模板的登录会话立即失效；服务端按产品版本、模板和数据归属计算最终权限。"));
            })
            .WithName("ListPermissionTemplates")
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
                ApiAuthorizationService authorizationService,
                IPermissionTemplateService service,
                ApiPermissionTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
                await SavePermissionTemplateAsync(
                    context,
                    tokenService,
                    authorizationService,
                    service,
                    request,
                    null,
                    cancellationToken))
            .WithName("CreatePermissionTemplate")
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
                ApiAuthorizationService authorizationService,
                IPermissionTemplateService service,
                int id,
                ApiPermissionTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
                await SavePermissionTemplateAsync(
                    context,
                    tokenService,
                    authorizationService,
                    service,
                    request,
                    id,
                    cancellationToken))
            .WithName("UpdatePermissionTemplate")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapDelete("/api/permission-templates/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IPermissionTemplateService service,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageUsers(user))
                {
                    return TypedForbidden("只有全功能版管理员可以管理权限模板。");
                }

                try
                {
                    bool deleted = await service.DeleteAsync(id, cancellationToken);
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
            ApiAuthorizationService authorizationService,
            IPermissionTemplateService service,
            ApiPermissionTemplateSaveRequest request,
            int? id,
            CancellationToken cancellationToken)
        {
            var user = ApiEndpointAuth.GetRequiredUser(context);

            if (!authorizationService.CanManageUsers(user))
            {
                return TypedForbidden("只有全功能版管理员可以管理权限模板。");
            }

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
                        (request.Modules ?? []).Select(module =>
                            new PermissionTemplateModuleRecord(module.ModuleKey, module.AccessLevel)).ToArray()),
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

        private static ApiPermissionModuleDefinitionDto ToApiDto(PermissionModuleDefinition module) =>
            new(module.Key, module.Name, module.Group, module.Workspace, module.SortOrder, module.IsTechnical);

        private static ApiPermissionTemplateDto ToApiDto(PermissionTemplateRecord template) =>
            new(
                template.Id,
                template.Code,
                template.Name,
                template.Description,
                template.IsSystem,
                template.IsActive,
                template.UpdatedAt,
                template.Modules.Select(module =>
                    new ApiPermissionTemplateModuleDto(module.ModuleKey, module.AccessLevel)).ToArray());
    }
}

using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPermissionTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/permission-templates", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IPermissionTemplateService service,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageUsers(user))
                    return WriteForbidden("只有全功能版管理员可以管理权限模板。");

                var templates = await service.ListAsync(cancellationToken);
                return Results.Ok(new ApiPermissionTemplateCatalogResponse(
                    PermissionModuleCatalog.Modules.Select(ToApiDto).ToArray(),
                    templates.Select(ToApiDto).ToArray(),
                    PermissionAccessLevel.Levels,
                    "模板修改后，已登录账号需要重新登录；服务端按产品版本、模板和数据归属计算最终权限。"));
            }).WithName("ListPermissionTemplates");

            endpoints.MapPost("/api/permission-templates", SaveTemplateAsync)
                .WithName("CreatePermissionTemplate");
            endpoints.MapPut("/api/permission-templates/{id:int}", SaveTemplateAsync)
                .WithName("UpdatePermissionTemplate");
            endpoints.MapDelete("/api/permission-templates/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IPermissionTemplateService service,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanManageUsers(user))
                    return WriteForbidden("只有全功能版管理员可以管理权限模板。");
                try
                {
                    return await service.DeleteAsync(id, cancellationToken)
                        ? Results.Ok(new ApiCommandResponse(true, "权限模板已删除。"))
                        : Results.NotFound(new ApiErrorResponse("未找到权限模板。"));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            }).WithName("DeletePermissionTemplate");
        }

        private static async Task<IResult> SaveTemplateAsync(
            HttpContext context,
            IApiSessionTokenService tokenService,
            ApiAuthorizationService authorizationService,
            IPermissionTemplateService service,
            ApiPermissionTemplateSaveRequest request,
            CancellationToken cancellationToken,
            int? id = null)
        {
            var user = ApiEndpointAuth.RequireUser(context, tokenService);
            if (user == null) return Results.Unauthorized();
            if (!authorizationService.CanManageUsers(user))
                return WriteForbidden("只有全功能版管理员可以管理权限模板。");
            if (request == null) return Results.BadRequest(new ApiErrorResponse("权限模板请求不能为空。"));
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
                return Results.Ok(ToApiDto(saved));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ApiErrorResponse(ex.Message));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new ApiErrorResponse("未找到权限模板。"));
            }
            catch (InvalidOperationException ex)
            {
                return WriteConflict(ex.Message);
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

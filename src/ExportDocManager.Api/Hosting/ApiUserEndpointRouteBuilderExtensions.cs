using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapUserEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/users", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                IPermissionTemplateService permissionTemplateService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return WriteForbidden("只有管理员可以管理用户账号。");
                }

                try
                {
                    var users = await userService.GetUsersAsync(cancellationToken);
                    var templates = await permissionTemplateService.ListAsync(cancellationToken);
                    return Results.Ok(new ApiUserListResponse(
                        users.Select(ApiUserManagementDtoFactory.FromUser).ToArray(),
                        UserRoleCatalog.Roles,
                        templates.Select(ToPermissionTemplateOptionDto).ToArray()));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
            })
            .WithName("ListUsers");

            endpoints.MapPost("/api/users", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                ApiUserSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return WriteForbidden("只有管理员可以管理用户账号。");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("用户请求体不能为空。"));
                }

                if (string.IsNullOrWhiteSpace(request.ResetPassword))
                {
                    return Results.BadRequest(new ApiErrorResponse("新增用户需要填写初始密码。"));
                }

                try
                {
                    UserPasswordPolicy.EnsureValid(request.ResetPassword, "初始密码");
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }

                try
                {
                    int savedUserId = await userService.SaveUserAsync(
                        ApiUserManagementDtoFactory.ToUser(request, 0),
                        request.ResetPassword ?? string.Empty,
                        cancellationToken);
                    var savedUser = await FindUserByIdAsync(userService, savedUserId, cancellationToken);
                    return Results.Ok(new ApiUserSaveResponse(
                        true,
                        "用户已保存。",
                        ApiUserManagementDtoFactory.FromUser(savedUser)));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreateUser");

            endpoints.MapPut("/api/users/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                int id,
                ApiUserSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return WriteForbidden("只有管理员可以管理用户账号。");
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("用户 ID 无效。"));
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("用户请求体不能为空。"));
                }

                if (!string.IsNullOrEmpty(request.ResetPassword))
                {
                    try
                    {
                        UserPasswordPolicy.EnsureValid(request.ResetPassword, "重置密码");
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new ApiErrorResponse(ex.Message));
                    }
                }

                try
                {
                    int savedUserId = await userService.SaveUserAsync(
                        ApiUserManagementDtoFactory.ToUser(request, id),
                        request.ResetPassword ?? string.Empty,
                        cancellationToken);
                    var savedUser = await FindUserByIdAsync(userService, savedUserId, cancellationToken);
                    return Results.Ok(new ApiUserSaveResponse(
                        true,
                        "用户已保存。",
                        ApiUserManagementDtoFactory.FromUser(savedUser)));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdateUser");

            endpoints.MapDelete("/api/users/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return WriteForbidden("只有管理员可以管理用户账号。");
                }

                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("用户 ID 无效。"));
                }

                try
                {
                    bool deleted = await userService.DeleteUserAsync(id, cancellationToken);
                    return deleted
                        ? Results.Ok(new ApiCommandResponse(true, "用户已删除。"))
                        : Results.NotFound(new ApiErrorResponse("未找到要删除的用户。"));
                }
                catch (UnauthorizedAccessException ex)
                {
                    return WriteForbidden(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteUser");
        }

        private static ApiPermissionTemplateOptionDto ToPermissionTemplateOptionDto(PermissionTemplateRecord template) =>
            new(template.Id, template.Code, template.Name, template.IsSystem, template.IsActive);
    }
}

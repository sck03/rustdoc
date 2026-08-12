using ExportDocManager.Services.Security;
using ExportDocManager.Services.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapUserEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/users", async Task<Results<
                Ok<ApiUserListResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                IPermissionTemplateService permissionTemplateService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user is null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return TypedForbidden("只有管理员可以管理用户账号。");
                }

                try
                {
                    var users = await userService.GetUsersAsync(cancellationToken);
                    var templates = await permissionTemplateService.ListAsync(cancellationToken);
                    return TypedResults.Ok(new ApiUserListResponse(
                        users.Select(ApiUserManagementDtoFactory.FromUser).ToArray(),
                        UserRoleCatalog.Roles,
                        templates.Select(ToPermissionTemplateOptionDto).ToArray()));
                }
                catch (PermissionDeniedException ex)
                {
                    return TypedForbidden(ex.Message);
                }
            })
            .WithName("ListUsers")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/users", async Task<Results<
                Ok<ApiUserSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                ApiUserSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user is null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return TypedForbidden("只有管理员可以管理用户账号。");
                }

                if (request is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("用户请求体不能为空。"));
                }

                if (string.IsNullOrWhiteSpace(request.ResetPassword))
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增用户需要填写初始密码。"));
                }

                try
                {
                    UserPasswordPolicy.EnsureValid(request.ResetPassword, "初始密码");
                    int savedUserId = await userService.SaveUserAsync(
                        ApiUserManagementDtoFactory.ToUser(request, 0),
                        request.ResetPassword ?? string.Empty,
                        cancellationToken);
                    var savedUser = await FindUserByIdAsync(userService, savedUserId, cancellationToken);
                    return TypedResults.Ok(new ApiUserSaveResponse(
                        true,
                        "用户已保存。",
                        ApiUserManagementDtoFactory.FromUser(savedUser)));
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
            })
            .WithName("createUserAccount")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/users/{id:int}", async Task<Results<
                Ok<ApiUserSaveResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                int id,
                ApiUserSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user is null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return TypedForbidden("只有管理员可以管理用户账号。");
                }

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("用户 ID 无效。"));
                }

                if (request is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("用户请求体不能为空。"));
                }

                try
                {
                    if (!string.IsNullOrEmpty(request.ResetPassword))
                    {
                        UserPasswordPolicy.EnsureValid(request.ResetPassword, "重置密码");
                    }

                    int savedUserId = await userService.SaveUserAsync(
                        ApiUserManagementDtoFactory.ToUser(request, id),
                        request.ResetPassword ?? string.Empty,
                        cancellationToken);
                    await tokenService.RevokeUserSessionsAsync(savedUserId, cancellationToken);
                    var savedUser = await FindUserByIdAsync(userService, savedUserId, cancellationToken);
                    return TypedResults.Ok(new ApiUserSaveResponse(
                        true,
                        "用户已保存。",
                        ApiUserManagementDtoFactory.FromUser(savedUser)));
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
            })
            .WithName("updateUserAccount")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapDelete("/api/users/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                JsonHttpResult<ApiErrorResponse>,
                NotFound<ApiErrorResponse>,
                Conflict<ApiErrorResponse>>>(
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserService userService,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user is null)
                {
                    return TypedResults.Unauthorized();
                }

                if (!authorizationService.CanManageUsers(user))
                {
                    return TypedForbidden("只有管理员可以管理用户账号。");
                }

                if (id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("用户 ID 无效。"));
                }

                try
                {
                    bool deleted = await userService.DeleteUserAsync(id, cancellationToken);
                    if (deleted)
                    {
                        await tokenService.RevokeUserSessionsAsync(id, cancellationToken);
                    }
                    return deleted
                        ? TypedResults.Ok(new ApiCommandResponse(true, "用户已删除。"))
                        : TypedResults.NotFound(new ApiErrorResponse("未找到要删除的用户。"));
                }
                catch (ServiceValidationException ex)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (PermissionDeniedException ex)
                {
                    return TypedForbidden(ex.Message);
                }
                catch (ResourceConflictException ex)
                {
                    return TypedResults.Conflict(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("deleteUserAccount")
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);
        }

        private static ApiPermissionTemplateOptionDto ToPermissionTemplateOptionDto(PermissionTemplateRecord template) =>
            new(template.Id, template.Code, template.Name, template.IsSystem, template.IsActive);
    }
}

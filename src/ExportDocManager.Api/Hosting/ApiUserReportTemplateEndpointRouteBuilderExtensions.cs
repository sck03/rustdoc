using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapUserReportTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/reports/user-templates", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                string reportType,
                bool includeInactive,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(
                        user,
                        PermissionModuleCatalog.DocumentReports,
                        PermissionAccessLevel.View))
                {
                    return WriteForbidden("当前权限模板不允许查看报表模板。");
                }

                if (!Enum.TryParse<ReportDocumentType>(reportType, true, out var parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                var rows = await service.ListAsync(parsedReportType, includeInactive, cancellationToken);
                return Results.Ok(rows.Select(ToApiDto));
            })
            .WithName("ListUserReportTemplates");

            endpoints.MapPost("/api/reports/user-templates", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                IReportTemplateService fileTemplateService,
                ApiUserReportTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(
                        user,
                        PermissionModuleCatalog.DocumentReports,
                        PermissionAccessLevel.Operate))
                {
                    return WriteForbidden("当前权限模板不允许新建设计模板。");
                }

                if (request == null || request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增报表模板不能包含已有 ID。"));
                }

                try
                {
                    string content = request.ContentHtml ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(content) &&
                        !string.IsNullOrWhiteSpace(request.SourceTemplatePath))
                    {
                        if (!Enum.TryParse<ReportDocumentType>(request.ReportType, true, out var reportType))
                        {
                            return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                        }

                        var source = await fileTemplateService.GetTemplateContentAsync(
                            reportType,
                            request.SourceTemplatePath,
                            cancellationToken);
                        content = source.Content;
                    }

                    var saved = await service.SaveAsync(ToSaveRequest(request, 0, content), cancellationToken);
                    return Results.Created($"/api/reports/user-templates/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (FileNotFoundException ex)
                {
                    return Results.NotFound(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("CreateUserReportTemplate");

            endpoints.MapPut("/api/reports/user-templates/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                int id,
                ApiUserReportTemplateSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(
                        user,
                        PermissionModuleCatalog.DocumentReports,
                        PermissionAccessLevel.Operate))
                {
                    return WriteForbidden("当前权限模板不允许修改设计模板。");
                }

                if (request == null || id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板 ID 无效。"));
                }

                try
                {
                    return Results.Ok(ToApiDto(await service.SaveAsync(
                        ToSaveRequest(request, id, request.ContentHtml), cancellationToken)));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (UserReportTemplateConcurrencyException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("UpdateUserReportTemplate");

            endpoints.MapDelete("/api/reports/user-templates/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(
                        user,
                        PermissionModuleCatalog.DocumentReports,
                        PermissionAccessLevel.Operate))
                {
                    return WriteForbidden("当前权限模板不允许删除设计模板。");
                }

                try
                {
                    return await service.DeleteAsync(id, cancellationToken)
                        ? Results.Ok(new ApiCommandResponse(true, "报表模板已删除。"))
                        : Results.NotFound();
                }
                catch (UserReportTemplateConcurrencyException ex)
                {
                    return Results.Conflict(new ApiErrorResponse(ex.Message));
                }
            })
            .WithName("DeleteUserReportTemplate");

            endpoints.MapGet("/api/reports/user-templates/{id:int}/versions", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                int id,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.View))
                    return WriteForbidden("当前权限模板不允许查看模板历史。");
                if (id <= 0) return Results.BadRequest(new ApiErrorResponse("报表模板 ID 无效。"));
                var rows = await service.ListVersionsAsync(id, cancellationToken);
                return rows.Count == 0 ? Results.NotFound() : Results.Ok(rows.Select(ToApiVersionDto));
            }).WithName("ListUserReportTemplateVersions");

            endpoints.MapPost("/api/reports/user-templates/{id:int}/versions/{versionNumber:int}/restore", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                int id,
                int versionNumber,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentReports, PermissionAccessLevel.Operate))
                    return WriteForbidden("当前权限模板不允许恢复历史版本。");
                if (id <= 0 || versionNumber <= 0)
                    return Results.BadRequest(new ApiErrorResponse("报表模板历史版本无效。"));
                try
                {
                    return Results.Ok(ToApiDto(await service.RestoreVersionAsync(id, versionNumber, cancellationToken)));
                }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RestoreUserReportTemplateVersion");
        }

        private static ApiUserReportTemplateDto ToApiDto(UserReportTemplateRecord item) =>
            new(item.Id, item.ReportType, item.Name, item.ContentHtml, item.IsActive,
                item.IsShared, item.ShareScope, item.VersionNumber, item.CanEdit, item.OwnerUserId);

        private static UserReportTemplateSaveRequest ToSaveRequest(
            ApiUserReportTemplateSaveRequest item,
            int id,
            string contentHtml) =>
            new(id, item.ReportType, item.Name, contentHtml, item.IsActive, item.IsShared, item.ShareScope, item.ExpectedVersion);

        private static ApiUserReportTemplateVersionDto ToApiVersionDto(UserReportTemplateVersionRecord item) =>
            new(item.Id, item.UserReportTemplateId, item.VersionNumber, item.ChangeType, item.Name,
                item.ContentHtml, item.IsActive, item.IsShared, item.ShareScope, item.ChangedBy, item.CreatedAt, item.CanRestore);
    }
}

using ExportDocManager.Services.Errors;
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
                ApiAuthorizationService authorizationService,
                IUserReportTemplateService service,
                string? reportType,
                bool? includeArchived,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.TryParse(reportType, true, out ReportDocumentType parsedReportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                if (includeArchived == true && !authorizationService.CanUsePermission(
                        ApiEndpointAuth.GetRequiredUser(context),
                        PermissionResourceCatalog.ReportTemplates,
                        PermissionAction.Restore))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                var rows = await service.ListAsync(parsedReportType, includeArchived ?? false, cancellationToken);
                return Results.Ok(rows.Select(ToApiDto));
            })
            .WithName("ListUserReportTemplates")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.View)
            .Produces<IReadOnlyList<ApiUserReportTemplateDto>>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/reports/user-templates", async (
                IUserReportTemplateService service,
                ApiUserReportTemplateCreateRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request is null)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增报表模板请求不能为空。"));
                }

                try
                {
                    var saved = await service.SaveDraftAsync(
                        new UserReportTemplateDraftRequest(
                            0,
                            request.ReportType,
                            request.Name,
                            request.ContentHtml ?? string.Empty),
                        cancellationToken);
                    return Results.Created($"/api/reports/user-templates/{saved.Id}", ToApiDto(saved));
                }
                catch (Exception exception) when (
                    exception is ServiceException or ArgumentException or FileNotFoundException)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("CreateUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Design)
            .Produces<ApiUserReportTemplateDto>(StatusCodes.Status201Created)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/user-templates/clone", async (
                HttpContext context,
                IUserReportTemplateService service,
                IReportHtmlService reportHtmlService,
                IReportTemplateService fileTemplateService,
                ApiUserReportTemplateCloneRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request is null)
                {
                    return Results.BadRequest(new ApiErrorResponse("复制报表模板请求不能为空。"));
                }
                if (!Enum.TryParse(request.ReportType, true, out ReportDocumentType reportType))
                {
                    return Results.BadRequest(new ApiErrorResponse("报表类型无效。"));
                }

                string sourceReference = (request.SourceTemplatePath ?? string.Empty)
                    .Trim()
                    .Replace('\\', '/');
                if (sourceReference.Length == 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("复制来源模板不能为空。"));
                }

                try
                {
                    UserReportTemplateCloneRequest command;
                    if (TryParseUserReportTemplateReference(sourceReference, out int sourceUserTemplateId))
                    {
                        command = new UserReportTemplateCloneRequest(
                            request.ReportType,
                            request.Name,
                            SourceUserTemplateId: sourceUserTemplateId);
                    }
                    else
                    {
                        if (!sourceReference.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
                        {
                            return Results.BadRequest(new ApiErrorResponse("只能从正式内置模板或有权查看的用户模板复制。"));
                        }

                        var availableTemplates = await reportHtmlService
                            .GetAvailableTemplatesAsync(reportType, cancellationToken);
                        var sourceDescriptor = availableTemplates.FirstOrDefault(template =>
                            string.Equals(
                                ToApiReportTemplatePath(context, template.TemplatePath),
                                sourceReference,
                                StringComparison.OrdinalIgnoreCase));
                        if (sourceDescriptor == null)
                        {
                            return Results.NotFound(new ApiErrorResponse("复制来源模板不存在或不在正式模板清单中。"));
                        }

                        var source = await fileTemplateService.GetTemplateContentAsync(
                            reportType,
                            sourceDescriptor.TemplatePath,
                            cancellationToken);
                        command = new UserReportTemplateCloneRequest(
                            request.ReportType,
                            request.Name,
                            ServerResolvedContentHtml: source.Content);
                    }

                    var saved = await service.CloneAsync(command, cancellationToken);
                    return Results.Created($"/api/reports/user-templates/{saved.Id}", ToApiDto(saved));
                }
                catch (Exception exception) when (
                    exception is ServiceException or ArgumentException or IOException or InvalidOperationException)
                {
                    return WriteServiceException(exception);
                }
            })
            .WithName("CloneUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Clone)
            .Produces<ApiUserReportTemplateDto>(StatusCodes.Status201Created)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/reports/user-templates/{id:int}/draft", async (
                IUserReportTemplateService service,
                int id,
                ApiUserReportTemplateDraftRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request is null || id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板 ID 无效。"));
                }

                return await ExecuteUserReportTemplateCommandAsync(
                    () => service.SaveDraftAsync(
                        ToDraftRequest(request, id, request.ContentHtml),
                        cancellationToken));
            })
            .WithName("SaveUserReportTemplateDraft")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Design)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/user-templates/{id:int}/publish", async (
                IUserReportTemplateService service,
                int id,
                ApiUserReportTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteUserReportTemplateCommandAsync(
                    () => service.PublishAsync(id, request?.ExpectedVersion ?? 0, cancellationToken)))
            .WithName("PublishUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Publish)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/user-templates/{id:int}/share", async (
                IUserReportTemplateService service,
                int id,
                ApiUserReportTemplateShareRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteUserReportTemplateCommandAsync(
                    () => service.ShareAsync(
                        id,
                        new UserReportTemplateShareRequest(
                            request?.ShareScope ?? string.Empty,
                            request?.ExpectedVersion ?? 0),
                        cancellationToken)))
            .WithName("ShareUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Share)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/user-templates/{id:int}/disable", async (
                IUserReportTemplateService service,
                int id,
                ApiUserReportTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteUserReportTemplateCommandAsync(
                    () => service.DisableAsync(id, request?.ExpectedVersion ?? 0, cancellationToken)))
            .WithName("DisableUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Deactivate)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/reports/user-templates/{id:int}/restore", async (
                IUserReportTemplateService service,
                int id,
                ApiUserReportTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteUserReportTemplateCommandAsync(
                    () => service.RestoreAsync(id, request?.ExpectedVersion ?? 0, cancellationToken)))
            .WithName("RestoreUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Restore)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/reports/user-templates/{id:int}", async (
                IUserReportTemplateService service,
                int id,
                int expectedVersion,
                CancellationToken cancellationToken) =>
                await ExecuteUserReportTemplateCommandAsync(
                    () => service.ArchiveAsync(id, expectedVersion, cancellationToken)))
            .WithName("ArchiveUserReportTemplate")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Archive)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapGet("/api/reports/user-templates/{id:int}/versions", async (
                IUserReportTemplateService service,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (id <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板 ID 无效。"));
                }
                var rows = await service.ListVersionsAsync(id, cancellationToken);
                return rows.Count == 0 ? Results.NotFound() : Results.Ok(rows.Select(ToApiVersionDto));
            })
            .WithName("ListUserReportTemplateVersions")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.View)
            .Produces<IReadOnlyList<ApiUserReportTemplateVersionDto>>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/reports/user-templates/{id:int}/versions/{versionNumber:int}/restore", async (
                IUserReportTemplateService service,
                int id,
                int versionNumber,
                ApiUserReportTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
            {
                if (id <= 0 || versionNumber <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("报表模板历史版本无效。"));
                }
                return await ExecuteUserReportTemplateCommandAsync(
                    () => service.RestoreVersionAsync(
                        id,
                        versionNumber,
                        request?.ExpectedVersion ?? 0,
                        cancellationToken));
            })
            .WithName("RestoreUserReportTemplateVersion")
            .WithApiCapability(PermissionResourceCatalog.ReportTemplates, PermissionAction.Restore)
            .Produces<ApiUserReportTemplateDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }

        private static async Task<IResult> ExecuteUserReportTemplateCommandAsync(
            Func<Task<UserReportTemplateRecord>> command)
        {
            try
            {
                return Results.Ok(ToApiDto(await command()));
            }
            catch (Exception exception) when (exception is ServiceException or ArgumentException)
            {
                return WriteServiceException(exception);
            }
        }

        private static ApiUserReportTemplateDto ToApiDto(UserReportTemplateRecord item) =>
            new(
                item.Id,
                item.ReportType,
                item.Name,
                item.ContentHtml,
                item.Status,
                item.ShareScope,
                item.VersionNumber,
                item.CanEdit,
                item.CanPublish,
                item.CanShare,
                item.CanDisable,
                item.CanRestore,
                item.CanArchive,
                item.OwnerUserId);

        private static UserReportTemplateDraftRequest ToDraftRequest(
            ApiUserReportTemplateDraftRequest item,
            int id,
            string contentHtml) =>
            new(id, item.ReportType, item.Name, contentHtml, item.ExpectedVersion);

        private static bool TryParseUserReportTemplateReference(string reference, out int id)
        {
            const string prefix = "user-template:";
            id = 0;
            return reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(reference[prefix.Length..], out id) &&
                   id > 0;
        }

        private static ApiUserReportTemplateVersionDto ToApiVersionDto(UserReportTemplateVersionRecord item) =>
            new(
                item.Id,
                item.UserReportTemplateId,
                item.VersionNumber,
                item.ChangeType,
                item.Name,
                item.ContentHtml,
                item.Status,
                item.ShareScope,
                item.ChangedBy,
                item.CreatedAt,
                item.CanRestore);
    }
}

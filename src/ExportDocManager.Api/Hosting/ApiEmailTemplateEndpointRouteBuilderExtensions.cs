using ExportDocManager.Services.EmailTemplates;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapEmailTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/email-templates", async Task<Results<Ok<IReadOnlyList<ApiEmailTemplateDto>>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>>> (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IEmailTemplateService service,
                string? keyword,
                string? category,
                bool? includeArchived,
                CancellationToken cancellationToken) =>
            {
                if (includeArchived == true)
                {
                    var user = ApiEndpointAuth.GetRequiredUser(context);
                    if (!authorizationService.CanUsePermission(
                            user,
                            PermissionResourceCatalog.EmailTemplates,
                            PermissionAction.Restore))
                    {
                        return TypedForbidden("查看归档邮件模板需要恢复权限。");
                    }
                }

                var rows = await service.ListAsync(keyword, category, includeArchived ?? false, cancellationToken);
                return TypedResults.Ok<IReadOnlyList<ApiEmailTemplateDto>>(rows.Select(ToApiDto).ToArray());
            }).WithName("ListEmailTemplates")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.View)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/email-templates/variables", Results<Ok<IReadOnlyList<ApiEmailTemplateVariableDto>>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>> (
                IEmailTemplateService service) =>
                TypedResults.Ok<IReadOnlyList<ApiEmailTemplateVariableDto>>(
                    service.ListVariables().Select(ToApiDto).ToArray()))
            .WithName("ListEmailTemplateVariables")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.View)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates/preview", Results<Ok<ApiEmailTemplatePreviewDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>> (
                IEmailTemplateService service,
                ApiEmailTemplatePreviewRequest request) =>
            {
                if (request is null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("预览请求不能为空。"));
                }

                try
                {
                    return TypedResults.Ok(ToApiDto(service.Preview(new EmailTemplatePreviewRequest(
                        request.Subject,
                        request.BodyHtml,
                        request.Variables ?? new Dictionary<string, string>()))));
                }
                catch (ArgumentException exception)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse(exception.Message));
                }
            }).WithName("PreviewEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.View)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates", async Task<Results<Created<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>>> (
                IEmailTemplateService service,
                ApiEmailTemplateDraftRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request is null || request.ExpectedVersion != 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增邮件模板不能包含已有版本号。"));
                }

                try
                {
                    var saved = await service.SaveDraftAsync(ToDraftRequest(request, 0), cancellationToken);
                    return TypedResults.Created($"/api/email-templates/{saved.Id}", ToApiDto(saved));
                }
                catch (PermissionDeniedException exception)
                {
                    return TypedForbidden(exception.Message);
                }
                catch (Exception exception) when (exception is ServiceValidationException or ArgumentException)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse(exception.Message));
                }
                catch (ResourceConflictException exception)
                {
                    return TypedResults.Conflict(new ApiErrorResponse(exception.Message));
                }
            }).WithName("CreateEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Edit)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/email-templates/{id:int}/draft", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                ApiEmailTemplateDraftRequest request,
                CancellationToken cancellationToken) =>
            {
                if (request is null || id <= 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("邮件模板 ID 无效。"));
                }

                return await ExecuteEmailTemplateCommandAsync(
                    () => service.SaveDraftAsync(ToDraftRequest(request, id), cancellationToken));
            }).WithName("SaveEmailTemplateDraft")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Edit)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates/{id:int}/publish", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                ApiEmailTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteEmailTemplateCommandAsync(
                    () => service.PublishAsync(id, request?.ExpectedVersion ?? 0, cancellationToken)))
            .WithName("PublishEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Publish)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates/{id:int}/share", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                ApiEmailTemplateShareRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteEmailTemplateCommandAsync(
                    () => service.ShareAsync(
                        id,
                        new EmailTemplateShareRequest(request?.ShareScope ?? string.Empty, request?.ExpectedVersion ?? 0),
                        cancellationToken)))
            .WithName("ShareEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Share)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates/{id:int}/disable", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                ApiEmailTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteEmailTemplateCommandAsync(
                    () => service.DisableAsync(id, request?.ExpectedVersion ?? 0, cancellationToken)))
            .WithName("DisableEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Deactivate)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates/{id:int}/restore", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                ApiEmailTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteEmailTemplateCommandAsync(
                    () => service.RestoreAsync(id, request?.ExpectedVersion ?? 0, cancellationToken)))
            .WithName("RestoreEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Restore)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapDelete("/api/email-templates/{id:int}", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                int expectedVersion,
                CancellationToken cancellationToken) =>
                await ExecuteEmailTemplateCommandAsync(
                    () => service.ArchiveAsync(id, expectedVersion, cancellationToken)))
            .WithName("ArchiveEmailTemplate")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Delete)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/email-templates/{id:int}/versions", async Task<Results<Ok<IReadOnlyList<ApiEmailTemplateVersionDto>>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                CancellationToken cancellationToken) =>
            {
                var rows = await service.ListVersionsAsync(id, cancellationToken);
                return rows.Count == 0
                    ? TypedResults.NotFound()
                    : TypedResults.Ok<IReadOnlyList<ApiEmailTemplateVersionDto>>(rows.Select(ToApiDto).ToArray());
            }).WithName("ListEmailTemplateVersions")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.View)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/email-templates/{id:int}/versions/{versionNumber:int}/restore", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> (
                IEmailTemplateService service,
                int id,
                int versionNumber,
                ApiEmailTemplateLifecycleRequest request,
                CancellationToken cancellationToken) =>
                await ExecuteEmailTemplateCommandAsync(
                    () => service.RestoreVersionAsync(
                        id,
                        versionNumber,
                        request?.ExpectedVersion ?? 0,
                        cancellationToken)))
            .WithName("RestoreEmailTemplateVersion")
            .WithApiCapability(PermissionResourceCatalog.EmailTemplates, PermissionAction.Restore)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden);
        }

        private static async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, JsonHttpResult<ApiErrorResponse>, Conflict<ApiErrorResponse>, NotFound>> ExecuteEmailTemplateCommandAsync(
            Func<Task<EmailTemplateRecord>> command)
        {
            try
            {
                return TypedResults.Ok(ToApiDto(await command()));
            }
            catch (PermissionDeniedException exception)
            {
                return TypedForbidden(exception.Message);
            }
            catch (ResourceNotFoundException)
            {
                return TypedResults.NotFound();
            }
            catch (Exception exception) when (exception is ServiceValidationException or ArgumentException)
            {
                return TypedResults.BadRequest(new ApiErrorResponse(exception.Message));
            }
            catch (Exception exception) when (exception is ResourceConflictException or ServiceConcurrencyException)
            {
                return TypedResults.Conflict(new ApiErrorResponse(exception.Message));
            }
        }

        private static ApiEmailTemplateDto ToApiDto(EmailTemplateRecord item) =>
            new(
                item.Id,
                item.Name,
                item.Category,
                item.Subject,
                item.BodyHtml,
                item.Status,
                item.ShareScope,
                item.VersionNumber,
                item.OwnerUserId,
                item.CanEdit,
                item.CanPublish,
                item.CanShare,
                item.CanDisable,
                item.CanRestore,
                item.CanArchive);

        private static ApiEmailTemplateVersionDto ToApiDto(EmailTemplateVersionRecord item) =>
            new(
                item.Id,
                item.EmailTemplateId,
                item.VersionNumber,
                item.ChangeType,
                item.Name,
                item.Category,
                item.Subject,
                item.BodyHtml,
                item.Status,
                item.ShareScope,
                item.ChangedBy,
                item.CreatedAt,
                item.CanRestore);

        private static ApiEmailTemplateVariableDto ToApiDto(EmailTemplateVariableRecord item) =>
            new(item.Key, item.Token, item.Label, item.SampleValue);

        private static ApiEmailTemplatePreviewDto ToApiDto(EmailTemplatePreview item) =>
            new(item.Subject, item.BodyHtml, item.UnresolvedTokens);

        private static EmailTemplateDraftRequest ToDraftRequest(ApiEmailTemplateDraftRequest item, int id) =>
            new(id, item.Name, item.Category, item.Subject, item.BodyHtml, item.ExpectedVersion);
    }
}

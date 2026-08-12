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
            endpoints.MapGet("/api/email-templates", async Task<Results<Ok<IReadOnlyList<ApiEmailTemplateDto>>, UnauthorizedHttpResult, ForbidHttpResult>>(
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, IEmailTemplateService service,
                string? keyword, string? category, bool? includeInactive, CancellationToken ct) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                var rows = await service.ListAsync(keyword, category, includeInactive ?? false, ct);
                return TypedResults.Ok<IReadOnlyList<ApiEmailTemplateDto>>(rows.Select(ToApiDto).ToArray());
            }).WithName("ListEmailTemplates");

            endpoints.MapGet("/api/email-templates/variables", Results<Ok<IReadOnlyList<ApiEmailTemplateVariableDto>>, UnauthorizedHttpResult, ForbidHttpResult> (
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, IEmailTemplateService service) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                return TypedResults.Ok<IReadOnlyList<ApiEmailTemplateVariableDto>>(
                    service.ListVariables().Select(ToApiDto).ToArray());
            }).WithName("ListEmailTemplateVariables");

            endpoints.MapPost("/api/email-templates/preview", Results<Ok<ApiEmailTemplatePreviewDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult> (
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, ApiEmailTemplatePreviewRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                if (request is null) return TypedResults.BadRequest(new ApiErrorResponse("预览请求不能为空。"));
                return TypedResults.Ok(ToApiDto(service.Preview(new EmailTemplatePreviewRequest(
                    request.Subject, request.BodyHtml, request.Variables ?? new Dictionary<string, string>()))));
            }).WithName("PreviewEmailTemplate");

            endpoints.MapPost("/api/email-templates", async Task<Results<Created<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>>>(
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, ApiEmailTemplateSaveRequest request, CancellationToken ct) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                if (request is null || request.Id > 0)
                    return TypedResults.BadRequest(new ApiErrorResponse("新增邮件模板不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveAsync(ToSaveRequest(request, 0), ct);
                    return TypedResults.Created($"/api/email-templates/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return TypedResults.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("CreateEmailTemplate");

            endpoints.MapPut("/api/email-templates/{id:int}", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>>(
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, IEmailTemplateService service,
                int id, ApiEmailTemplateSaveRequest request, CancellationToken ct) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                if (request is null || id <= 0)
                    return TypedResults.BadRequest(new ApiErrorResponse("邮件模板ID无效。"));
                try { return TypedResults.Ok(ToApiDto(await service.SaveAsync(ToSaveRequest(request, id), ct))); }
                catch (ArgumentException ex) { return TypedResults.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return TypedResults.NotFound(); }
            }).WithName("UpdateEmailTemplate");

            endpoints.MapGet("/api/email-templates/{id:int}/versions", async Task<Results<Ok<IReadOnlyList<ApiEmailTemplateVersionDto>>, UnauthorizedHttpResult, ForbidHttpResult, NotFound>>(
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, int id, CancellationToken ct) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                var rows = await service.ListVersionsAsync(id, ct);
                return rows.Count == 0
                    ? TypedResults.NotFound()
                    : TypedResults.Ok<IReadOnlyList<ApiEmailTemplateVersionDto>>(rows.Select(ToApiDto).ToArray());
            }).WithName("ListEmailTemplateVersions");

            endpoints.MapPost("/api/email-templates/{id:int}/versions/{versionNumber:int}/restore", async Task<Results<Ok<ApiEmailTemplateDto>, BadRequest<ApiErrorResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>>(
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, IEmailTemplateService service,
                int id, int versionNumber, CancellationToken ct) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                if (id <= 0 || versionNumber <= 0)
                    return TypedResults.BadRequest(new ApiErrorResponse("邮件模板历史版本无效。"));
                try { return TypedResults.Ok(ToApiDto(await service.RestoreVersionAsync(id, versionNumber, ct))); }
                catch (ArgumentException ex) { return TypedResults.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return TypedResults.NotFound(); }
            }).WithName("RestoreEmailTemplateVersion");

            endpoints.MapDelete("/api/email-templates/{id:int}", async Task<Results<Ok<ApiCommandResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiErrorResponse>, NotFound>>(
                HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, int id, CancellationToken ct) =>
            {
                var user = ApiEndpointAuth.RequireUser(c, t);
                if (user is null) return TypedResults.Unauthorized();
                if (!a.CanUseSalesWorkspace(user)) return TypedResults.Forbid();
                try
                {
                    return await service.DeleteAsync(id, ct)
                        ? TypedResults.Ok(new ApiCommandResponse(true, "邮件模板已删除。"))
                        : TypedResults.NotFound();
                }
                catch (ResourceConflictException ex) { return TypedResults.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteEmailTemplate");
        }

        private static ApiEmailTemplateDto ToApiDto(EmailTemplateRecord item) =>
            new(item.Id, item.Name, item.Category, item.Subject, item.BodyHtml, item.IsActive, item.IsShared,
                item.VersionNumber, item.CanEdit);
        private static ApiEmailTemplateVersionDto ToApiDto(EmailTemplateVersionRecord item) =>
            new(item.Id, item.EmailTemplateId, item.VersionNumber, item.ChangeType, item.Name, item.Category,
                item.Subject, item.BodyHtml, item.IsActive, item.IsShared, item.ChangedBy, item.CreatedAt, item.CanRestore);
        private static ApiEmailTemplateVariableDto ToApiDto(EmailTemplateVariableRecord item) =>
            new(item.Key, item.Token, item.Label, item.SampleValue);
        private static ApiEmailTemplatePreviewDto ToApiDto(EmailTemplatePreview item) =>
            new(item.Subject, item.BodyHtml, item.UnresolvedTokens);
        private static EmailTemplateSaveRequest ToSaveRequest(ApiEmailTemplateSaveRequest item, int id) =>
            new(id, item.Name, item.Category, item.Subject, item.BodyHtml, item.IsActive, item.IsShared,
                item.ExpectedVersion);
    }
}

using ExportDocManager.Services.EmailTemplates;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapEmailTemplateEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/email-templates", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                bool includeInactive = bool.TryParse(c.Request.Query["includeInactive"], out bool parsed) && parsed;
                var rows = await service.ListAsync(c.Request.Query["keyword"], c.Request.Query["category"], includeInactive, ct);
                return Results.Ok(rows.Select(ToApiDto));
            }).WithName("ListEmailTemplates");
            endpoints.MapGet("/api/email-templates/variables", (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service) => HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok(service.ListVariables().Select(ToApiDto)) : denied).WithName("ListEmailTemplateVariables");
            endpoints.MapPost("/api/email-templates/preview", (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, ApiEmailTemplatePreviewRequest request) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (request == null) return Results.BadRequest(new ApiErrorResponse("预览请求不能为空。"));
                return Results.Ok(ToApiDto(service.Preview(new EmailTemplatePreviewRequest(
                    request.Subject, request.BodyHtml, request.Variables ?? new Dictionary<string, string>()))));
            }).WithName("PreviewEmailTemplate");
            endpoints.MapPost("/api/email-templates", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, ApiEmailTemplateSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增邮件模板不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveAsync(ToSaveRequest(request, 0), ct);
                    return Results.Created($"/api/email-templates/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("CreateEmailTemplate");
            endpoints.MapPut("/api/email-templates/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, int id, ApiEmailTemplateSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (request == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("邮件模板ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveAsync(ToSaveRequest(request, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateEmailTemplate");
            endpoints.MapGet("/api/email-templates/{id:int}/versions", async (HttpContext c, IApiSessionTokenService t,
                ApiAuthorizationService a, IEmailTemplateService service, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                var rows = await service.ListVersionsAsync(id, ct);
                return rows.Count > 0 ? Results.Ok(rows.Select(ToApiDto)) : Results.NotFound();
            }).WithName("ListEmailTemplateVersions");
            endpoints.MapPost("/api/email-templates/{id:int}/versions/{versionNumber:int}/restore", async (HttpContext c,
                IApiSessionTokenService t, ApiAuthorizationService a, IEmailTemplateService service, int id,
                int versionNumber, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (id <= 0 || versionNumber <= 0) return Results.BadRequest(new ApiErrorResponse("邮件模板历史版本无效。"));
                try { return Results.Ok(ToApiDto(await service.RestoreVersionAsync(id, versionNumber, ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("RestoreEmailTemplateVersion");
            endpoints.MapDelete("/api/email-templates/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                IEmailTemplateService service, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                return await service.DeleteAsync(id, ct)
                    ? Results.Ok(new ApiCommandResponse(true, "邮件模板已删除。")) : Results.NotFound();
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
            new(id, item.Name, item.Category, item.Subject, item.BodyHtml, item.IsActive, item.IsShared);
    }
}

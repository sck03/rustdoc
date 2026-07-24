using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Opportunities;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapCrmEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/crm/dashboard", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, ISalesOpportunityService opportunities, CancellationToken ct) =>
                HasSalesAccess(context, tokens, auth, out var denied)
                    ? Results.Ok(ToApiDto(await service.GetDashboardAsync(ct), await opportunities.GetDashboardAsync(ct)))
                    : denied).WithName("GetCrmDashboard");

            endpoints.MapGet("/api/crm/customers", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, CancellationToken ct) =>
                HasSalesAccess(context, tokens, auth, out var denied)
                    ? Results.Ok((await service.ListCustomersAsync(ct)).Select(ToApiDto))
                    : denied).WithName("ListCrmCustomers");

            endpoints.MapGet("/api/crm/customers/page", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, string keyword, string status,
                int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                var page = await service.QueryCustomersAsync(keyword, status, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiCrmCustomerDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QueryCrmCustomers");

            endpoints.MapPost("/api/crm/customers/batch-status", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, ApiCrmCustomerBatchStatusRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                try
                {
                    int affected = await service.UpdateCustomerStatusAsync(request?.Ids ?? [], request?.Status ?? string.Empty, ct);
                    return Results.Ok(new ApiCrmCustomerBatchStatusResult(affected, request.Status));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("UpdateCrmCustomerBatchStatus");

            endpoints.MapGet("/api/crm/customers/export", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmCustomerExportService exportService, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                byte[] content = await exportService.ExportAsync(context.Request.Query["keyword"], context.Request.Query["status"], ct);
                return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"crm-customers-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
            }).WithName("ExportCrmCustomers");

            endpoints.MapPost("/api/crm/customers", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, ApiCrmCustomerSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增 CRM 客户不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveCustomerAsync(ToSaveRequest(request, 0), ct);
                    return Results.Created($"/api/crm/customers/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("CreateCrmCustomer");

            endpoints.MapPut("/api/crm/customers/{id:int}", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, int id, ApiCrmCustomerSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request == null || id <= 0 || (request.Id > 0 && request.Id != id))
                    return Results.BadRequest(new ApiErrorResponse("CRM 客户ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveCustomerAsync(ToSaveRequest(request, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateCrmCustomer");

            endpoints.MapDelete("/api/crm/customers/{id:int}", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                try
                {
                    return await service.DeleteCustomerAsync(id, ct)
                        ? Results.Ok(new ApiCommandResponse(true, "CRM 客户已删除。"))
                        : Results.NotFound();
                }
                catch (InvalidOperationException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteCrmCustomer");

            endpoints.MapPost("/api/crm/import/preview", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmCustomerImportService importService, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                string fileName = context.Request.Query["fileName"].ToString();
                try
                {
                    using var input = new MemoryStream();
                    await ApiUploadLimits.CopyRequestBodyAsync(
                        context.Request,
                        input,
                        ApiUploadLimits.CrmImportBytes,
                        ct);
                    if (input.Length == 0)
                        return Results.BadRequest(new ApiErrorResponse("CRM 导入文件为空。"));
                    input.Position = 0;
                    return Results.Ok(ToApiDto(await importService.PreviewAsync(input, fileName, ct)));
                }
                catch (PayloadLimitExceededException ex) { return WritePayloadTooLarge(ex); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("PreviewCrmCustomerImport");

            endpoints.MapPost("/api/crm/import", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmCustomerImportService importService,
                ApiCrmCustomerImportRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request?.Rows == null || request.Rows.Count == 0 || request.Rows.Count > 5000)
                    return Results.BadRequest(new ApiErrorResponse("请选择 1 至 5000 行有效客户数据。"));
                var result = await importService.ImportAsync(request.Rows.Select(ToImportRow).ToArray(), ct);
                return Results.Ok(new ApiCrmCustomerImportResultDto(
                    result.CreatedCustomers, result.CreatedContacts, result.SkippedDuplicates));
            }).WithName("ImportCrmCustomers");

            endpoints.MapGet("/api/crm/customers/{customerId:int}/email-variable-draft", async (HttpContext context,
                IApiSessionTokenService tokens, ApiAuthorizationService auth, ICrmService service, int customerId, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                try { return Results.Ok(ToApiDto(await service.GetEmailVariableDraftAsync(customerId, ct))); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("GetCrmEmailVariableDraft");

            endpoints.MapGet("/api/crm/customers/{customerId:int}/contacts", async (HttpContext context,
                IApiSessionTokenService tokens, ApiAuthorizationService auth, ICrmService service, int customerId, CancellationToken ct) =>
                HasSalesAccess(context, tokens, auth, out var denied)
                    ? Results.Ok((await service.ListContactsAsync(customerId, ct)).Select(ToApiDto))
                    : denied).WithName("ListCrmContacts");

            endpoints.MapPost("/api/crm/customers/{customerId:int}/contacts", async (HttpContext context,
                IApiSessionTokenService tokens, ApiAuthorizationService auth, ICrmService service, int customerId,
                ApiCrmContactSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增联系人不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveContactAsync(ToSaveRequest(request, customerId, 0), ct);
                    return Results.Created($"/api/crm/customers/{customerId}/contacts/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateCrmContact");

            endpoints.MapPut("/api/crm/customers/{customerId:int}/contacts/{id:int}", async (HttpContext context,
                IApiSessionTokenService tokens, ApiAuthorizationService auth, ICrmService service, int customerId, int id,
                ApiCrmContactSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request == null || id <= 0 || (request.Id > 0 && request.Id != id))
                    return Results.BadRequest(new ApiErrorResponse("联系人ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveContactAsync(ToSaveRequest(request, customerId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateCrmContact");

            endpoints.MapDelete("/api/crm/customers/{customerId:int}/contacts/{id:int}", async (HttpContext context,
                IApiSessionTokenService tokens, ApiAuthorizationService auth, ICrmService service, int customerId, int id,
                CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                try
                {
                    return await service.DeleteContactAsync(customerId, id, ct)
                        ? Results.Ok(new ApiCommandResponse(true, "联系人已删除，历史跟进仍保留。"))
                        : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteCrmContact");

            endpoints.MapGet("/api/crm/follow-ups", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, int? crmCustomerId, bool includeCompleted,
                int? limit, CancellationToken ct) =>
                HasSalesAccess(context, tokens, auth, out var denied)
                    ? Results.Ok((await service.ListFollowUpsAsync(crmCustomerId, includeCompleted, limit ?? 100, ct)).Select(ToApiDto))
                    : denied).WithName("ListCrmFollowUps");

            endpoints.MapGet("/api/crm/follow-ups/page", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, int? crmCustomerId, bool includeCompleted,
                int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                var page = await service.QueryFollowUpsAsync(crmCustomerId, includeCompleted, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiCrmFollowUpDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QueryCrmFollowUps");

            endpoints.MapPost("/api/crm/follow-ups", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, ApiCrmFollowUpSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增跟进不能包含已有ID。"));
                try { return Results.Ok(ToApiDto(await service.SaveFollowUpAsync(ToSaveRequest(request, 0), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateCrmFollowUp");

            endpoints.MapPut("/api/crm/follow-ups/{id:int}", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, int id, ApiCrmFollowUpSaveRequest request, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                if (request == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("跟进记录ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveFollowUpAsync(ToSaveRequest(request, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateCrmFollowUp");

            endpoints.MapDelete("/api/crm/follow-ups/{id:int}", async (HttpContext context, IApiSessionTokenService tokens,
                ApiAuthorizationService auth, ICrmService service, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(context, tokens, auth, out var denied)) return denied;
                try
                {
                    return await service.DeleteFollowUpAsync(id, ct)
                        ? Results.Ok(new ApiCommandResponse(true, "跟进记录已删除。"))
                        : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteCrmFollowUp");
        }

        private static bool HasSalesAccess(HttpContext context, IApiSessionTokenService tokens,
            ApiAuthorizationService auth, out IResult denied)
        {
            var user = ApiEndpointAuth.RequireUser(context, tokens);
            denied = user == null ? Results.Unauthorized() : Results.Forbid();
            return user != null && auth.CanUseSalesWorkspace(user);
        }

        private static ApiCrmCustomerDto ToApiDto(CrmCustomerRecord item) =>
            new(item.Id, item.Name, item.CountryRegion, item.Website, item.Status, item.Source, item.Notes,
                item.LinkedDocumentCustomerId, item.VersionNumber);
        private static ApiCrmContactDto ToApiDto(CrmContactRecord item) =>
            new(item.Id, item.CrmCustomerId, item.Name, item.Title, item.Email, item.Phone,
                item.InstantMessaging, item.IsPrimary, item.VersionNumber);
        private static ApiCrmFollowUpDto ToApiDto(CrmFollowUpRecord item) =>
            new(item.Id, item.CrmCustomerId, item.CustomerName, item.CrmContactId, item.ContactName, item.Type,
                item.Summary, item.NextAction, item.FollowedUpAt, item.NextFollowUpAt, item.IsCompleted,
                item.CreatedAt, item.UpdatedAt, item.VersionNumber);
        private static ApiCrmDashboardDto ToApiDto(CrmDashboardRecord item, SalesOpportunityDashboard opportunities) =>
            new(item.CustomerCount, item.ContactCount, item.PendingFollowUpCount, item.OverdueFollowUpCount,
                item.DueNextSevenDaysCount, item.UpcomingFollowUps.Select(ToApiDto).ToArray(),
                opportunities.Stages.Select(value => new ApiSalesOpportunityStageSummaryDto(value.Stage, value.Count)).ToArray(),
                opportunities.Currencies.Select(value => new ApiSalesOpportunityCurrencySummaryDto(
                    value.Currency, value.Count, value.EstimatedAmount, value.WeightedAmount)).ToArray(),
                opportunities.UpcomingClosings.Select(ToApiDto).ToArray());
        private static ApiCrmCustomerImportPreviewDto ToApiDto(CrmCustomerImportPreview item) =>
            new(item.TotalRows, item.ValidRows, item.DuplicateRows, item.Rows.Select(ToApiDto).ToArray());
        private static ApiCrmCustomerImportRowDto ToApiDto(CrmCustomerImportRow item) =>
            new(item.RowNumber, item.Name, item.CountryRegion, item.Website, item.Status, item.Source, item.Notes,
                item.ContactName, item.ContactTitle, item.ContactEmail, item.ContactPhone, item.IsDuplicate, item.Error);
        private static ApiCrmEmailVariableDraftDto ToApiDto(CrmEmailVariableDraft item) =>
            new(item.CrmCustomerId, item.CrmContactId, item.ToAddress, item.Variables);
        private static CrmCustomerImportRow ToImportRow(ApiCrmCustomerImportRowDto item) =>
            new(item.RowNumber, item.Name, item.CountryRegion, item.Website, item.Status, item.Source, item.Notes,
                item.ContactName, item.ContactTitle, item.ContactEmail, item.ContactPhone, item.IsDuplicate, item.Error);
        private static CrmCustomerSaveRequest ToSaveRequest(ApiCrmCustomerSaveRequest item, int id) =>
            new(id, item.Name, item.CountryRegion, item.Website, item.Status, item.Source, item.Notes,
                item.LinkedDocumentCustomerId, item.ExpectedVersion);
        private static CrmContactSaveRequest ToSaveRequest(ApiCrmContactSaveRequest item, int customerId, int id) =>
            new(id, customerId, item.Name, item.Title, item.Email, item.Phone, item.InstantMessaging,
                item.IsPrimary, item.ExpectedVersion);
        private static CrmFollowUpSaveRequest ToSaveRequest(ApiCrmFollowUpSaveRequest item, int id) =>
            new(id, item.CrmCustomerId, item.CrmContactId, item.Type, item.Summary, item.NextAction,
                item.FollowedUpAt, item.NextFollowUpAt, item.IsCompleted, item.ExpectedVersion);
    }
}

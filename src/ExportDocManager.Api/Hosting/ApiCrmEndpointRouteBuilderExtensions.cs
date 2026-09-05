using ExportDocManager.Services.Crm;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Opportunities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapCrmEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/crm/dashboard", async (ICrmService service,
                ISalesOpportunityService opportunities, CancellationToken ct) =>
                Results.Ok(ToApiDto(await service.GetDashboardAsync(ct), await opportunities.GetDashboardAsync(ct))))
            .WithName("GetCrmDashboard")
            .WithApiCapabilities(
                new(PermissionResourceCatalog.SalesDashboard, PermissionAction.View),
                new(PermissionResourceCatalog.CrmCustomers, PermissionAction.View),
                new(PermissionResourceCatalog.SalesOpportunities, PermissionAction.View))
            .Produces<ApiCrmDashboardDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/crm/customers/page", async (ICrmService service, string? keyword, string? status,
                int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await service.QueryCustomersAsync(keyword, status, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiCrmCustomerDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QueryCrmCustomers")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiCrmCustomerDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/crm/customers/batch-status", async (ICrmService service,
                ApiCrmCustomerBatchStatusRequest request, CancellationToken ct) =>
            {
                try
                {
                    int affected = await service.UpdateCustomerStatusAsync(request?.Ids ?? [], request?.Status ?? string.Empty, ct);
                    return Results.Ok(new ApiCrmCustomerBatchStatusResult(affected, request?.Status ?? string.Empty));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("UpdateCrmCustomerBatchStatus")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Deactivate)
            .Produces<ApiCrmCustomerBatchStatusResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/crm/customers/export", async (ICrmCustomerExportService exportService,
                string? keyword, string? status,
                IBusinessClock clock, CancellationToken ct) =>
            {
                byte[] content = await exportService.ExportAsync(keyword, status, ct);
                return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"crm-customers-{clock.UtcNow:yyyyMMdd-HHmmss}.xlsx");
            }).WithName("ExportCrmCustomers")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Export)
            .Produces<byte[]>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/crm/customers", async (ICrmService service,
                ApiCrmCustomerSaveRequest request, CancellationToken ct) =>
            {
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增 CRM 客户不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveCustomerAsync(ToSaveRequest(request, 0), ct);
                    return Results.Created($"/api/crm/customers/{saved.Id}", ToApiDto(saved));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("CreateCrmCustomer")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Create)
            .Produces<ApiCrmCustomerDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/crm/customers/{id:int}", async (ICrmService service, int id,
                ApiCrmCustomerSaveRequest request, CancellationToken ct) =>
            {
                if (request == null || id <= 0 || (request.Id > 0 && request.Id != id))
                    return Results.BadRequest(new ApiErrorResponse("CRM 客户ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveCustomerAsync(ToSaveRequest(request, id), ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("UpdateCrmCustomer")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Edit)
            .Produces<ApiCrmCustomerDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/crm/customers/{id:int}/deactivate", async (
                ICrmService service, int id, ApiCrmLifecycleRequest request, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await service.DeactivateCustomerAsync(id, request?.ExpectedVersion ?? 0, ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("DeactivateCrmCustomer")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Deactivate)
            .Produces<ApiCrmCustomerDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/crm/customers/{id:int}/restore", async (
                ICrmService service, int id, ApiCrmLifecycleRequest request, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await service.RestoreCustomerAsync(id, request?.ExpectedVersion ?? 0, ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("RestoreCrmCustomer")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Deactivate)
            .Produces<ApiCrmCustomerDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/crm/customers/{id:int}", async (ICrmService service, int id,
                int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await service.DeleteCustomerAsync(id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "CRM 客户已删除。"))
                        : Results.NotFound();
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("DeleteCrmCustomer")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/crm/import/preview", async (HttpContext context,
                ICrmCustomerImportService importService, string? fileName, CancellationToken ct) =>
            {
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
                    return Results.Ok(ToApiDto(await importService.PreviewAsync(input, fileName ?? string.Empty, ct)));
                }
                catch (PayloadLimitExceededException ex) { return WritePayloadTooLarge(ex); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).Accepts<IFormFile>("application/octet-stream").WithName("PreviewCrmCustomerImport")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Import)
            .Produces<ApiCrmCustomerImportPreviewDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/crm/import", async (ICrmCustomerImportService importService,
                ApiCrmCustomerImportRequest request, CancellationToken ct) =>
            {
                if (request == null || !Guid.TryParseExact(request.PreviewId, "N", out _))
                    return Results.BadRequest(new ApiErrorResponse("客户导入预检编号无效，请重新选择文件。"));
                var result = await importService.ImportAsync(request.PreviewId, ct);
                return Results.Ok(new ApiCrmCustomerImportResultDto(
                    result.CreatedCustomers, result.CreatedContacts, result.SkippedDuplicates));
            }).WithName("ImportCrmCustomers")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.Import)
            .Produces<ApiCrmCustomerImportResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapGet("/api/crm/customers/{customerId:int}/email-variable-draft", async (
                ICrmService service, int customerId, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await service.GetEmailVariableDraftAsync(customerId, ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("GetCrmEmailVariableDraft")
            .WithApiCapability(PermissionResourceCatalog.CrmCustomers, PermissionAction.View)
            .Produces<ApiCrmEmailVariableDraftDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/api/crm/customers/{customerId:int}/contacts", async (
                ICrmService service, int customerId, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await service.QueryContactsAsync(customerId, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiCrmContactDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            })
            .WithName("QueryCrmContacts")
            .WithApiCapability(PermissionResourceCatalog.CrmContacts, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiCrmContactDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/crm/customers/{customerId:int}/contacts", async (ICrmService service, int customerId,
                ApiCrmContactSaveRequest request, CancellationToken ct) =>
            {
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增联系人不能包含已有ID。"));
                try
                {
                    var saved = await service.SaveContactAsync(ToSaveRequest(request, customerId, 0), ct);
                    return Results.Created($"/api/crm/customers/{customerId}/contacts/{saved.Id}", ToApiDto(saved));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("CreateCrmContact")
            .WithApiCapability(PermissionResourceCatalog.CrmContacts, PermissionAction.Create)
            .Produces<ApiCrmContactDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/crm/customers/{customerId:int}/contacts/{id:int}", async (
                ICrmService service, int customerId, int id,
                ApiCrmContactSaveRequest request, CancellationToken ct) =>
            {
                if (request == null || id <= 0 || (request.Id > 0 && request.Id != id))
                    return Results.BadRequest(new ApiErrorResponse("联系人ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveContactAsync(ToSaveRequest(request, customerId, id), ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("UpdateCrmContact")
            .WithApiCapability(PermissionResourceCatalog.CrmContacts, PermissionAction.Edit)
            .Produces<ApiCrmContactDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/crm/customers/{customerId:int}/contacts/{id:int}/set-primary", async (
                ICrmService service, int customerId, int id, ApiCrmLifecycleRequest request, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(ToApiDto(await service.SetPrimaryContactAsync(
                        customerId, id, request?.ExpectedVersion ?? 0, ct)));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("SetPrimaryCrmContact")
            .WithApiCapability(PermissionResourceCatalog.CrmContacts, PermissionAction.SetPrimary)
            .Produces<ApiCrmContactDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/crm/customers/{customerId:int}/contacts/{id:int}", async (
                ICrmService service, int customerId, int id,
                int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await service.DeleteContactAsync(customerId, id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "联系人已删除，历史跟进仍保留。"))
                        : Results.NotFound();
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("DeleteCrmContact")
            .WithApiCapability(PermissionResourceCatalog.CrmContacts, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapGet("/api/crm/follow-ups/page", async (ICrmService service,
                int? crmCustomerId, bool? includeCompleted,
                int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await service.QueryFollowUpsAsync(crmCustomerId, includeCompleted ?? false, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiCrmFollowUpDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QueryCrmFollowUps")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiCrmFollowUpDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/crm/follow-ups", async (ICrmService service,
                ApiCrmFollowUpSaveRequest request, CancellationToken ct) =>
            {
                if (request == null || request.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增跟进不能包含已有ID。"));
                try { return Results.Ok(ToApiDto(await service.SaveFollowUpAsync(ToSaveRequest(request, 0), ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("CreateCrmFollowUp")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.Create)
            .Produces<ApiCrmFollowUpDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPut("/api/crm/follow-ups/{id:int}", async (ICrmService service, int id,
                ApiCrmFollowUpSaveRequest request, CancellationToken ct) =>
            {
                if (request == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("跟进记录ID无效。"));
                try { return Results.Ok(ToApiDto(await service.SaveFollowUpAsync(ToSaveRequest(request, id), ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("UpdateCrmFollowUp")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.Edit)
            .Produces<ApiCrmFollowUpDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPost("/api/crm/follow-ups/{id:int}/complete", async (
                ICrmService service, int id, ApiCrmLifecycleRequest request, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await service.CompleteFollowUpAsync(id, request?.ExpectedVersion ?? 0, ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("CompleteCrmFollowUp")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.Complete)
            .Produces<ApiCrmFollowUpDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/crm/follow-ups/{id:int}/restore", async (
                ICrmService service, int id, ApiCrmLifecycleRequest request, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await service.RestoreFollowUpAsync(id, request?.ExpectedVersion ?? 0, ct))); }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("RestoreCrmFollowUp")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.Restore)
            .Produces<ApiCrmFollowUpDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/crm/follow-ups/{id:int}/transfer", async (
                ICrmService service, int id, ApiCrmFollowUpTransferRequest request, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(ToApiDto(await service.TransferFollowUpAsync(
                        id,
                        new CrmFollowUpTransferRequest(
                            request?.CrmCustomerId ?? 0,
                            request?.CrmContactId,
                            request?.ExpectedVersion ?? 0),
                        ct)));
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("TransferCrmFollowUp")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.Assign)
            .WithApiSecurityAudit("crm-follow-up-transfer")
            .Produces<ApiCrmFollowUpDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/crm/follow-ups/{id:int}", async (ICrmService service, int id,
                int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await service.DeleteFollowUpAsync(id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "跟进记录已删除。"))
                        : Results.NotFound();
                }
                catch (ServiceException ex) { return WriteServiceException(ex); }
            }).WithName("DeleteCrmFollowUp")
            .WithApiCapability(PermissionResourceCatalog.CrmFollowUps, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
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
            new(item.TotalRows, item.ValidRows, item.DuplicateRows, item.Rows.Select(ToApiDto).ToArray(), item.PreviewId);
        private static ApiCrmCustomerImportRowDto ToApiDto(CrmCustomerImportRow item) =>
            new(item.RowNumber, item.Name, item.CountryRegion, item.Website, item.Status, item.Source, item.Notes,
                item.ContactName, item.ContactTitle, item.ContactEmail, item.ContactPhone, item.IsDuplicate, item.Error);
        private static ApiCrmEmailVariableDraftDto ToApiDto(CrmEmailVariableDraft item) =>
            new(item.CrmCustomerId, item.CrmContactId, item.ToAddress, item.Variables);
        private static CrmCustomerSaveRequest ToSaveRequest(ApiCrmCustomerSaveRequest item, int id) =>
            new(id, item.Name, item.CountryRegion, item.Website, item.Source, item.Notes,
                item.LinkedDocumentCustomerId, item.ExpectedVersion);
        private static CrmContactSaveRequest ToSaveRequest(ApiCrmContactSaveRequest item, int customerId, int id) =>
            new(id, customerId, item.Name, item.Title, item.Email, item.Phone, item.InstantMessaging,
                item.ExpectedVersion);
        private static CrmFollowUpSaveRequest ToSaveRequest(ApiCrmFollowUpSaveRequest item, int id) =>
            new(id, item.CrmCustomerId, item.CrmContactId, item.Type, item.Summary, item.NextAction,
                item.FollowedUpAt, item.NextFollowUpAt, item.ExpectedVersion);
    }
}

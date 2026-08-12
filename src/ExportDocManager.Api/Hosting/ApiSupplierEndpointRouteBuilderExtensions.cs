using ExportDocManager.Services.Suppliers;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSupplierEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/suppliers", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied) ? Results.Ok((await s.ListAsync(ct)).Select(ToApiDto)) : denied).WithName("ListSuppliers")
            .Produces<IReadOnlyList<ApiSupplierDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/page", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s,
                string? keyword, string? status, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                var page = await s.QueryAsync(keyword, status, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSupplierDto>(page.Items.Select(ToApiDto).ToArray(), page.TotalCount,
                    page.PageNumber, page.PageSize, page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySuppliers")
            .Produces<ApiPagedResponse<ApiSupplierDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s,
                ApiSupplierSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增供应商不能包含已有ID。"));
                try { var saved = await s.SaveAsync(ToSaveRequest(r, 0), ct); return Results.Created($"/api/suppliers/{saved.Id}", ToApiDto(saved)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("CreateSupplier")
            .Produces<ApiSupplierDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPut("/api/suppliers/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s,
                int id, ApiSupplierSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveAsync(ToSaveRequest(r, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplier")
            .Produces<ApiSupplierDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapDelete("/api/suppliers/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try
                {
                    var result = await s.DeleteAsync(id, ct);
                    if (!result.Found) return Results.NotFound();
                    string message = result.Deactivated
                        ? $"该供应商已有 {result.ProductLinkCount} 条供货关系和 {result.AssessmentCount} 条评价，系统已保留历史资料并将供应商停用。"
                        : $"供应商已删除，同时删除 {result.ContactCount} 位联系人。";
                    return Results.Ok(new ApiSupplierDeleteResponse(
                        true,
                        result.Deleted,
                        result.Deactivated,
                        message,
                        result.ContactCount,
                        result.ProductLinkCount,
                        result.AssessmentCount));
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplier")
            .Produces<ApiSupplierDeleteResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPost("/api/suppliers/batch-status", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, ApiSupplierBatchStatusRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try { int affected = await s.UpdateStatusAsync(r?.Ids ?? [], r?.Status ?? string.Empty, ct); return Results.Ok(new ApiSupplierBatchStatusResult(affected, r?.Status ?? string.Empty)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("UpdateSupplierBatchStatus")
            .Produces<ApiSupplierBatchStatusResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/import/preview", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierFileService files, string? fileName, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try
                {
                    using var input = new MemoryStream();
                    await ApiUploadLimits.CopyRequestBodyAsync(c.Request, input, ApiUploadLimits.SupplierImportBytes, ct);
                    if (input.Length == 0) return Results.BadRequest(new ApiErrorResponse("导入文件为空。"));
                    input.Position = 0; return Results.Ok(ToApiDto(await files.PreviewAsync(input, fileName ?? string.Empty, ct)));
                }
                catch (PayloadLimitExceededException ex) { return WritePayloadTooLarge(ex); }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).Accepts<IFormFile>("application/octet-stream").WithName("PreviewSupplierImport")
            .Produces<ApiSupplierImportPreviewDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/import", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierFileService files, ApiSupplierImportRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r?.Rows == null || r.Rows.Count == 0 || r.Rows.Count > 5000) return Results.BadRequest(new ApiErrorResponse("请选择 1 至 5000 行供应商数据。"));
                var result = await files.ImportAsync(r.Rows.Select(ToImportRow).ToArray(), ct);
                return Results.Ok(new ApiSupplierImportResultDto(result.CreatedSuppliers, result.CreatedContacts, result.SkippedRows));
            }).WithName("ImportSuppliers")
            .Produces<ApiSupplierImportResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/export", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierFileService files, string? keyword, string? status, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                byte[] content = await files.ExportAsync(keyword, status, ct);
                return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"suppliers-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
            }).WithName("ExportSuppliers")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/product-options", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, string? keyword, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok((await s.SearchProductsAsync(keyword, ct)).Select(ToApiDto))
                    : denied).WithName("SearchSupplierProductOptions")
            .Produces<IReadOnlyList<ApiSupplierProductOptionDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/assessment-overview", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok(ToApiDto(await s.GetOverviewAsync(ct)))
                    : denied).WithName("GetSupplierAssessmentOverview")
            .Produces<ApiSupplierAssessmentOverviewDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/{supplierId:int}/products", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok((await s.ListProductLinksAsync(supplierId, ct)).Select(ToApiDto))
                    : denied).WithName("ListSupplierProductLinks")
            .Produces<IReadOnlyList<ApiSupplierProductLinkDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/products", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, ApiSupplierProductLinkSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增供应商产品关联不能包含已有ID。"));
                try
                {
                    var saved = await s.SaveProductLinkAsync(ToSaveRequest(r, supplierId, 0), ct);
                    return Results.Created($"/api/suppliers/{supplierId}/products/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierProductLink")
            .Produces<ApiSupplierProductLinkDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPut("/api/suppliers/{supplierId:int}/products/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierProductLinkSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商产品关联ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveProductLinkAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierProductLink")
            .Produces<ApiSupplierProductLinkDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/products/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try
                {
                    return await s.DeleteProductLinkAsync(supplierId, id, ct)
                        ? Results.Ok(new ApiCommandResponse(true, "供应商产品关联已删除。")) : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplierProductLink")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapGet("/api/suppliers/{supplierId:int}/assessments", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok((await s.ListAsync(supplierId, ct)).Select(ToApiDto))
                    : denied).WithName("ListSupplierAssessments")
            .Produces<IReadOnlyList<ApiSupplierAssessmentDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/assessments", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, ApiSupplierAssessmentSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增供应商评价不能包含已有ID。"));
                try
                {
                    var saved = await s.SaveAsync(ToSaveRequest(r, supplierId, 0), ct);
                    return Results.Created($"/api/suppliers/{supplierId}/assessments/{saved.Id}", ToApiDto(saved));
                }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierAssessment")
            .Produces<ApiSupplierAssessmentDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPut("/api/suppliers/{supplierId:int}/assessments/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, int id, ApiSupplierAssessmentSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商评价ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierAssessment")
            .Produces<ApiSupplierAssessmentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/assessments/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try
                {
                    return await s.DeleteAsync(supplierId, id, ct)
                        ? Results.Ok(new ApiCommandResponse(true, "供应商评价已删除。"))
                        : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplierAssessment")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapGet("/api/suppliers/{supplierId:int}/contacts", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied) ? Results.Ok((await s.ListContactsAsync(supplierId, ct)).Select(ToApiDto)) : denied).WithName("ListSupplierContacts")
            .Produces<IReadOnlyList<ApiSupplierContactDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/contacts", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, ApiSupplierContactSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增联系人不能包含已有ID。"));
                try { var saved = await s.SaveContactAsync(ToSaveRequest(r, supplierId, 0), ct); return Results.Created($"/api/suppliers/{supplierId}/contacts/{saved.Id}", ToApiDto(saved)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierContact")
            .Produces<ApiSupplierContactDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPut("/api/suppliers/{supplierId:int}/contacts/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierContactSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("联系人ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveContactAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierContact")
            .Produces<ApiSupplierContactDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/contacts/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try
                {
                    return await s.DeleteContactAsync(supplierId, id, ct)
                        ? Results.Ok(new ApiCommandResponse(true, "联系人已删除。")) : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplierContact")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
        }

        private static ApiSupplierDto ToApiDto(SupplierRecord x) => new(x.Id, x.Name, x.CountryRegion, x.Category,
            x.Website, x.Status, x.MainProducts, x.Notes, x.VersionNumber);
        private static ApiSupplierContactDto ToApiDto(SupplierContactRecord x) => new(x.Id, x.SupplierCompanyId,
            x.Name, x.Title, x.Email, x.Phone, x.InstantMessaging, x.IsPrimary, x.VersionNumber);
        private static ApiSupplierProductOptionDto ToApiDto(SupplierProductOptionRecord x) => new(x.Id, x.ProductCode, x.NameCN, x.NameEN);
        private static ApiSupplierProductLinkDto ToApiDto(SupplierProductLinkRecord x) => new(x.Id, x.SupplierCompanyId, x.ProductId,
            x.ProductCode, x.ProductNameCN, x.ProductNameEN, x.SupplierProductCode, x.ReferencePrice,
            x.Currency, x.LeadTimeDays, x.Status, x.VersionNumber);
        private static ApiSupplierAssessmentDto ToApiDto(SupplierAssessmentRecord x) => new(
            x.Id, x.SupplierCompanyId, x.AssessedAt, x.AssessmentKind, x.QualityScore, x.DeliveryScore,
            x.ServiceScore, x.PriceScore, x.AverageScore, x.Conclusion, x.Notes, x.AssessedBy,
            x.CreatedAt, x.UpdatedAt, x.VersionNumber);
        private static ApiSupplierAssessmentOverviewDto ToApiDto(SupplierAssessmentOverview x) => new(
            x.TotalSuppliers, x.AssessedSuppliers, x.UnassessedSuppliers,
            x.PreferredCount, x.QualifiedCount, x.WatchCount, x.PausedCount,
            x.AverageQualityScore, x.AverageDeliveryScore, x.AverageServiceScore, x.AveragePriceScore,
            x.Items.Select(ToApiDto).ToArray());
        private static ApiSupplierAssessmentOverviewItemDto ToApiDto(SupplierAssessmentOverviewItem x) => new(
            x.SupplierCompanyId, x.SupplierName, x.SupplierStatus, x.Category, x.AssessmentCount,
            x.LatestAssessedAt, x.LatestAssessmentKind, x.QualityScore, x.DeliveryScore,
            x.ServiceScore, x.PriceScore, x.AverageScore, x.Conclusion, x.Notes);
        private static SupplierSaveRequest ToSaveRequest(ApiSupplierSaveRequest x, int id) => new(id, x.Name,
            x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes, x.ExpectedVersion);
        private static SupplierContactSaveRequest ToSaveRequest(ApiSupplierContactSaveRequest x, int supplierId, int id) =>
            new(id, supplierId, x.Name, x.Title, x.Email, x.Phone, x.InstantMessaging, x.IsPrimary,
                x.ExpectedVersion);
        private static ApiSupplierImportPreviewDto ToApiDto(SupplierImportPreview x) => new(x.TotalRows, x.ValidRows, x.DuplicateRows, x.Rows.Select(ToApiDto).ToArray());
        private static ApiSupplierImportRowDto ToApiDto(SupplierImportRow x) => new(x.RowNumber, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes, x.ContactName, x.ContactTitle, x.ContactEmail, x.ContactPhone, x.IsDuplicate, x.Error);
        private static SupplierImportRow ToImportRow(ApiSupplierImportRowDto x) => new(x.RowNumber, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes, x.ContactName, x.ContactTitle, x.ContactEmail, x.ContactPhone, x.IsDuplicate, x.Error);
        private static SupplierProductLinkSaveRequest ToSaveRequest(ApiSupplierProductLinkSaveRequest x, int supplierCompanyId, int id) =>
            new(id, supplierCompanyId, x.ProductId, x.SupplierProductCode, x.ReferencePrice, x.Currency,
                x.LeadTimeDays, x.Status, x.ExpectedVersion);
        private static SupplierAssessmentSaveRequest ToSaveRequest(ApiSupplierAssessmentSaveRequest x, int supplierCompanyId, int id) =>
            new(id, supplierCompanyId, x.AssessedAt, x.AssessmentKind, x.QualityScore, x.DeliveryScore,
                x.ServiceScore, x.PriceScore, x.Conclusion, x.Notes, x.ExpectedVersion);
    }
}

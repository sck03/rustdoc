using ExportDocManager.Services.Suppliers;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSupplierEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/suppliers", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied) ? Results.Ok((await s.ListAsync(ct)).Select(ToApiDto)) : denied).WithName("ListSuppliers");
            endpoints.MapGet("/api/suppliers/page", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s,
                string keyword, string status, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                var page = await s.QueryAsync(keyword, status, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSupplierDto>(page.Items.Select(ToApiDto).ToArray(), page.TotalCount,
                    page.PageNumber, page.PageSize, page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySuppliers");
            endpoints.MapPost("/api/suppliers", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s,
                ApiSupplierSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增供应商不能包含已有ID。"));
                try { var saved = await s.SaveAsync(ToSaveRequest(r, 0), ct); return Results.Created($"/api/suppliers/{saved.Id}", ToApiDto(saved)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("CreateSupplier");
            endpoints.MapPut("/api/suppliers/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s,
                int id, ApiSupplierSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveAsync(ToSaveRequest(r, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplier");
            endpoints.MapDelete("/api/suppliers/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a, ISupplierDirectoryService s, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                return await s.DeleteAsync(id, ct) ? Results.Ok(new ApiCommandResponse(true, "供应商已删除。")) : Results.NotFound();
            }).WithName("DeleteSupplier");
            endpoints.MapPost("/api/suppliers/batch-status", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, ApiSupplierBatchStatusRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                try { int affected = await s.UpdateStatusAsync(r?.Ids ?? [], r?.Status ?? string.Empty, ct); return Results.Ok(new ApiSupplierBatchStatusResult(affected, r.Status)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("UpdateSupplierBatchStatus");
            endpoints.MapPost("/api/suppliers/import/preview", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierFileService files, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (c.Request.ContentLength is > 10_485_760) return Results.BadRequest(new ApiErrorResponse("供应商导入文件不能超过 10 MB。"));
                try
                {
                    using var input = new MemoryStream(); await c.Request.Body.CopyToAsync(input, ct);
                    if (input.Length == 0 || input.Length > 10_485_760) return Results.BadRequest(new ApiErrorResponse("导入文件为空或超过 10 MB。"));
                    input.Position = 0; return Results.Ok(ToApiDto(await files.PreviewAsync(input, c.Request.Query["fileName"].ToString(), ct)));
                }
                catch (InvalidDataException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("PreviewSupplierImport");
            endpoints.MapPost("/api/suppliers/import", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierFileService files, ApiSupplierImportRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r?.Rows == null || r.Rows.Count == 0 || r.Rows.Count > 5000) return Results.BadRequest(new ApiErrorResponse("请选择 1 至 5000 行供应商数据。"));
                var result = await files.ImportAsync(r.Rows.Select(ToImportRow).ToArray(), ct);
                return Results.Ok(new ApiSupplierImportResultDto(result.CreatedSuppliers, result.CreatedContacts, result.SkippedRows));
            }).WithName("ImportSuppliers");
            endpoints.MapGet("/api/suppliers/export", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierFileService files, string keyword, string status, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                byte[] content = await files.ExportAsync(keyword, status, ct);
                return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"suppliers-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
            }).WithName("ExportSuppliers");
            endpoints.MapGet("/api/suppliers/product-options", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, string keyword, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok((await s.SearchProductsAsync(keyword, ct)).Select(ToApiDto))
                    : denied).WithName("SearchSupplierProductOptions");
            endpoints.MapGet("/api/suppliers/assessment-overview", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok(ToApiDto(await s.GetOverviewAsync(ct)))
                    : denied).WithName("GetSupplierAssessmentOverview");
            endpoints.MapGet("/api/suppliers/{supplierId:int}/products", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok((await s.ListProductLinksAsync(supplierId, ct)).Select(ToApiDto))
                    : denied).WithName("ListSupplierProductLinks");
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
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierProductLink");
            endpoints.MapPut("/api/suppliers/{supplierId:int}/products/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierProductLinkSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商产品关联ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveProductLinkAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierProductLink");
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/products/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                return await s.DeleteProductLinkAsync(supplierId, id, ct)
                    ? Results.Ok(new ApiCommandResponse(true, "供应商产品关联已删除。")) : Results.NotFound();
            }).WithName("DeleteSupplierProductLink");
            endpoints.MapGet("/api/suppliers/{supplierId:int}/assessments", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied)
                    ? Results.Ok((await s.ListAsync(supplierId, ct)).Select(ToApiDto))
                    : denied).WithName("ListSupplierAssessments");
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
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierAssessment");
            endpoints.MapPut("/api/suppliers/{supplierId:int}/assessments/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, int id, ApiSupplierAssessmentSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商评价ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierAssessment");
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/assessments/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierAssessmentService s, int supplierId, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                return await s.DeleteAsync(supplierId, id, ct)
                    ? Results.Ok(new ApiCommandResponse(true, "供应商评价已删除。"))
                    : Results.NotFound();
            }).WithName("DeleteSupplierAssessment");
            endpoints.MapGet("/api/suppliers/{supplierId:int}/contacts", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, CancellationToken ct) =>
                HasSalesAccess(c, t, a, out var denied) ? Results.Ok((await s.ListContactsAsync(supplierId, ct)).Select(ToApiDto)) : denied).WithName("ListSupplierContacts");
            endpoints.MapPost("/api/suppliers/{supplierId:int}/contacts", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, ApiSupplierContactSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增联系人不能包含已有ID。"));
                try { var saved = await s.SaveContactAsync(ToSaveRequest(r, supplierId, 0), ct); return Results.Created($"/api/suppliers/{supplierId}/contacts/{saved.Id}", ToApiDto(saved)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierContact");
            endpoints.MapPut("/api/suppliers/{supplierId:int}/contacts/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierContactSaveRequest r, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("联系人ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveContactAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierContact");
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/contacts/{id:int}", async (HttpContext c, IApiSessionTokenService t, ApiAuthorizationService a,
                ISupplierDirectoryService s, int supplierId, int id, CancellationToken ct) =>
            {
                if (!HasSalesAccess(c, t, a, out var denied)) return denied;
                return await s.DeleteContactAsync(supplierId, id, ct) ? Results.Ok(new ApiCommandResponse(true, "联系人已删除。")) : Results.NotFound();
            }).WithName("DeleteSupplierContact");
        }

        private static ApiSupplierDto ToApiDto(SupplierRecord x) => new(x.Id, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes);
        private static ApiSupplierContactDto ToApiDto(SupplierContactRecord x) => new(x.Id, x.SupplierCompanyId, x.Name, x.Title, x.Email, x.Phone, x.InstantMessaging, x.IsPrimary);
        private static ApiSupplierProductOptionDto ToApiDto(SupplierProductOptionRecord x) => new(x.Id, x.ProductCode, x.NameCN, x.NameEN);
        private static ApiSupplierProductLinkDto ToApiDto(SupplierProductLinkRecord x) => new(x.Id, x.SupplierCompanyId, x.ProductId,
            x.ProductCode, x.ProductNameCN, x.ProductNameEN, x.SupplierProductCode, x.ReferencePrice, x.Currency, x.LeadTimeDays, x.Status);
        private static ApiSupplierAssessmentDto ToApiDto(SupplierAssessmentRecord x) => new(
            x.Id, x.SupplierCompanyId, x.AssessedAt, x.AssessmentKind, x.QualityScore, x.DeliveryScore,
            x.ServiceScore, x.PriceScore, x.AverageScore, x.Conclusion, x.Notes, x.AssessedBy,
            x.CreatedAt, x.UpdatedAt);
        private static ApiSupplierAssessmentOverviewDto ToApiDto(SupplierAssessmentOverview x) => new(
            x.TotalSuppliers, x.AssessedSuppliers, x.UnassessedSuppliers,
            x.PreferredCount, x.QualifiedCount, x.WatchCount, x.PausedCount,
            x.AverageQualityScore, x.AverageDeliveryScore, x.AverageServiceScore, x.AveragePriceScore,
            x.Items.Select(ToApiDto).ToArray());
        private static ApiSupplierAssessmentOverviewItemDto ToApiDto(SupplierAssessmentOverviewItem x) => new(
            x.SupplierCompanyId, x.SupplierName, x.SupplierStatus, x.Category, x.AssessmentCount,
            x.LatestAssessedAt, x.LatestAssessmentKind, x.QualityScore, x.DeliveryScore,
            x.ServiceScore, x.PriceScore, x.AverageScore, x.Conclusion, x.Notes);
        private static SupplierSaveRequest ToSaveRequest(ApiSupplierSaveRequest x, int id) => new(id, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes);
        private static SupplierContactSaveRequest ToSaveRequest(ApiSupplierContactSaveRequest x, int supplierId, int id) => new(id, supplierId, x.Name, x.Title, x.Email, x.Phone, x.InstantMessaging, x.IsPrimary);
        private static ApiSupplierImportPreviewDto ToApiDto(SupplierImportPreview x) => new(x.TotalRows, x.ValidRows, x.DuplicateRows, x.Rows.Select(ToApiDto).ToArray());
        private static ApiSupplierImportRowDto ToApiDto(SupplierImportRow x) => new(x.RowNumber, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes, x.ContactName, x.ContactTitle, x.ContactEmail, x.ContactPhone, x.IsDuplicate, x.Error);
        private static SupplierImportRow ToImportRow(ApiSupplierImportRowDto x) => new(x.RowNumber, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes, x.ContactName, x.ContactTitle, x.ContactEmail, x.ContactPhone, x.IsDuplicate, x.Error);
        private static SupplierProductLinkSaveRequest ToSaveRequest(ApiSupplierProductLinkSaveRequest x, int supplierCompanyId, int id) =>
            new(id, supplierCompanyId, x.ProductId, x.SupplierProductCode, x.ReferencePrice, x.Currency, x.LeadTimeDays, x.Status);
        private static SupplierAssessmentSaveRequest ToSaveRequest(ApiSupplierAssessmentSaveRequest x, int supplierCompanyId, int id) =>
            new(id, supplierCompanyId, x.AssessedAt, x.AssessmentKind, x.QualityScore, x.DeliveryScore,
                x.ServiceScore, x.PriceScore, x.Conclusion, x.Notes);
    }
}

using ExportDocManager.Services.Suppliers;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapSupplierEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/suppliers/page", async (ISupplierDirectoryService s,
                string? keyword, string? status, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await s.QueryAsync(keyword, status, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSupplierDto>(page.Items.Select(ToApiDto).ToArray(), page.TotalCount,
                    page.PageNumber, page.PageSize, page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySuppliers")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiSupplierDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers", async (ISupplierDirectoryService s,
                ApiSupplierSaveRequest r, CancellationToken ct) =>
            {
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增供应商不能包含已有ID。"));
                try { var saved = await s.SaveAsync(ToSaveRequest(r, 0), ct); return Results.Created($"/api/suppliers/{saved.Id}", ToApiDto(saved)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("CreateSupplier")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Create)
            .Produces<ApiSupplierDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPut("/api/suppliers/{id:int}", async (ISupplierDirectoryService s,
                int id, ApiSupplierSaveRequest r, CancellationToken ct) =>
            {
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveAsync(ToSaveRequest(r, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplier")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Edit)
            .Produces<ApiSupplierDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPost("/api/suppliers/{id:int}/admit", async (
                ISupplierDirectoryService s, int id, ApiSupplierLifecycleRequest r, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await s.AdmitAsync(id, r?.ExpectedVersion ?? 0, ct))); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("AdmitSupplier")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Admit)
            .WithApiSecurityAudit("supplier-admit")
            .Produces<ApiSupplierDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapPost("/api/suppliers/{id:int}/deactivate", async (
                ISupplierDirectoryService s, int id, ApiSupplierLifecycleRequest r, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await s.DeactivateAsync(id, r?.ExpectedVersion ?? 0, ct))); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("DeactivateSupplier")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Deactivate)
            .Produces<ApiSupplierDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapPost("/api/suppliers/{id:int}/restore", async (
                ISupplierDirectoryService s, int id, ApiSupplierLifecycleRequest r, CancellationToken ct) =>
            {
                try { return Results.Ok(ToApiDto(await s.RestoreAsync(id, r?.ExpectedVersion ?? 0, ct))); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("RestoreSupplier")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Deactivate)
            .Produces<ApiSupplierDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapDelete("/api/suppliers/{id:int}", async (ISupplierDirectoryService s, int id, int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await s.DeleteAsync(id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "供应商已删除。"))
                        : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplier")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapPost("/api/suppliers/import/preview", async (HttpContext c, ISupplierFileService files,
                string? fileName, CancellationToken ct) =>
            {
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
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Import)
            .Produces<ApiSupplierImportPreviewDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/import", async (ISupplierFileService files,
                ApiSupplierImportRequest r, CancellationToken ct) =>
            {
                if (r == null || !Guid.TryParseExact(r.PreviewId, "N", out _)) return Results.BadRequest(new ApiErrorResponse("供应商导入预检编号无效，请重新选择文件。"));
                var result = await files.ImportAsync(r.PreviewId, ct);
                return Results.Ok(new ApiSupplierImportResultDto(result.CreatedSuppliers, result.CreatedContacts, result.SkippedRows));
            }).WithName("ImportSuppliers")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Import)
            .Produces<ApiSupplierImportResultDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/export", async (ISupplierFileService files, string? keyword,
                string? status, IBusinessClock clock, CancellationToken ct) =>
            {
                byte[] content = await files.ExportAsync(keyword, status, ct);
                return Results.File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"suppliers-{clock.UtcNow:yyyyMMdd-HHmmss}.xlsx");
            }).WithName("ExportSuppliers")
            .WithApiCapability(PermissionResourceCatalog.Suppliers, PermissionAction.Export)
            .Produces<byte[]>(StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/product-options", async (ISupplierDirectoryService s,
                string? keyword, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await s.SearchProductsAsync(keyword, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSupplierProductOptionDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("SearchSupplierProductOptions")
            .WithApiCapability(PermissionModuleCatalog.CommonProductReference, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiSupplierProductOptionDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/assessment-overview", async (ISupplierAssessmentService s,
                CancellationToken ct) =>
                Results.Ok(ToApiDto(await s.GetOverviewAsync(ct)))).WithName("GetSupplierAssessmentOverview")
            .WithApiCapability(PermissionResourceCatalog.SupplierAssessments, PermissionAction.View)
            .Produces<ApiSupplierAssessmentOverviewDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapGet("/api/suppliers/{supplierId:int}/products", async (ISupplierDirectoryService s,
                int supplierId, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await s.QueryProductLinksAsync(supplierId, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSupplierProductLinkDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySupplierProductLinks")
            .WithApiCapability(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiSupplierProductLinkDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/products", async (ISupplierDirectoryService s,
                int supplierId, ApiSupplierProductLinkSaveRequest r, CancellationToken ct) =>
            {
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
            .WithApiCapability(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.Edit)
            .Produces<ApiSupplierProductLinkDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPut("/api/suppliers/{supplierId:int}/products/{id:int}", async (ISupplierDirectoryService s,
                int supplierId, int id, ApiSupplierProductLinkSaveRequest r, CancellationToken ct) =>
            {
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商产品关联ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveProductLinkAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierProductLink")
            .WithApiCapability(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.Edit)
            .Produces<ApiSupplierProductLinkDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/products/{id:int}/deactivate", async (
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierLifecycleRequest r,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(ToApiDto(await s.DeactivateProductLinkAsync(
                        supplierId, id, r?.ExpectedVersion ?? 0, ct)));
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("DeactivateSupplierProductLink")
            .WithApiCapability(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.Deactivate)
            .Produces<ApiSupplierProductLinkDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/products/{id:int}/restore", async (
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierLifecycleRequest r,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(ToApiDto(await s.RestoreProductLinkAsync(
                        supplierId, id, r?.ExpectedVersion ?? 0, ct)));
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("RestoreSupplierProductLink")
            .WithApiCapability(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.Deactivate)
            .Produces<ApiSupplierProductLinkDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/products/{id:int}", async (ISupplierDirectoryService s,
                int supplierId, int id, int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await s.DeleteProductLinkAsync(supplierId, id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "供应商产品关联已删除。")) : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplierProductLink")
            .WithApiCapability(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapGet("/api/suppliers/{supplierId:int}/assessments", async (ISupplierAssessmentService s,
                int supplierId, CancellationToken ct) =>
                Results.Ok((await s.ListAsync(supplierId, ct)).Select(ToApiDto))).WithName("ListSupplierAssessments")
            .WithApiCapability(PermissionResourceCatalog.SupplierAssessments, PermissionAction.View)
            .Produces<IReadOnlyList<ApiSupplierAssessmentDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/assessments", async (ISupplierAssessmentService s,
                int supplierId, ApiSupplierAssessmentSaveRequest r, CancellationToken ct) =>
            {
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
            .WithApiCapability(PermissionResourceCatalog.SupplierAssessments, PermissionAction.Create)
            .Produces<ApiSupplierAssessmentDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPut("/api/suppliers/{supplierId:int}/assessments/{id:int}", async (ISupplierAssessmentService s,
                int supplierId, int id, ApiSupplierAssessmentSaveRequest r, CancellationToken ct) =>
            {
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("供应商评价ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierAssessment")
            .WithApiCapability(PermissionResourceCatalog.SupplierAssessments, PermissionAction.Edit)
            .Produces<ApiSupplierAssessmentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/assessments/{id:int}/confirm", async (
                ISupplierAssessmentService s, int supplierId, int id, int? expectedVersion, CancellationToken ct) =>
            {
                if (supplierId <= 0 || id <= 0 || expectedVersion is null or <= 0)
                    return Results.BadRequest(new ApiErrorResponse("确认供应商评价时必须提供有效版本号。"));
                try { return Results.Ok(ToApiDto(await s.ConfirmAsync(supplierId, id, expectedVersion.Value, ct))); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("ConfirmSupplierAssessment")
            .WithApiCapability(PermissionResourceCatalog.SupplierAssessments, PermissionAction.Approve)
            .Produces<ApiSupplierAssessmentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/assessments/{id:int}", async (ISupplierAssessmentService s,
                int supplierId, int id, int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await s.DeleteAsync(supplierId, id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "供应商评价已删除。"))
                        : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (ResourceConflictException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplierAssessment")
            .WithApiCapability(PermissionResourceCatalog.SupplierAssessments, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapGet("/api/suppliers/{supplierId:int}/contacts", async (ISupplierDirectoryService s,
                int supplierId, int? pageNumber, int? pageSize, CancellationToken ct) =>
            {
                var page = await s.QueryContactsAsync(supplierId, pageNumber ?? 1, pageSize ?? 20, ct);
                return Results.Ok(new ApiPagedResponse<ApiSupplierContactDto>(
                    page.Items.Select(ToApiDto).ToArray(), page.TotalCount, page.PageNumber, page.PageSize,
                    page.TotalPages, page.HasPreviousPage, page.HasNextPage));
            }).WithName("QuerySupplierContacts")
            .WithApiCapability(PermissionResourceCatalog.SupplierContacts, PermissionAction.View)
            .Produces<ApiPagedResponse<ApiSupplierContactDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/contacts", async (ISupplierDirectoryService s,
                int supplierId, ApiSupplierContactSaveRequest r, CancellationToken ct) =>
            {
                if (r == null || r.Id > 0) return Results.BadRequest(new ApiErrorResponse("新增联系人不能包含已有ID。"));
                try { var saved = await s.SaveContactAsync(ToSaveRequest(r, supplierId, 0), ct); return Results.Created($"/api/suppliers/{supplierId}/contacts/{saved.Id}", ToApiDto(saved)); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("CreateSupplierContact")
            .WithApiCapability(PermissionResourceCatalog.SupplierContacts, PermissionAction.Create)
            .Produces<ApiSupplierContactDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPut("/api/suppliers/{supplierId:int}/contacts/{id:int}", async (ISupplierDirectoryService s,
                int supplierId, int id, ApiSupplierContactSaveRequest r, CancellationToken ct) =>
            {
                if (r == null || id <= 0) return Results.BadRequest(new ApiErrorResponse("联系人ID无效。"));
                try { return Results.Ok(ToApiDto(await s.SaveContactAsync(ToSaveRequest(r, supplierId, id), ct))); }
                catch (ArgumentException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("UpdateSupplierContact")
            .WithApiCapability(PermissionResourceCatalog.SupplierContacts, PermissionAction.Edit)
            .Produces<ApiSupplierContactDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
            endpoints.MapPost("/api/suppliers/{supplierId:int}/contacts/{id:int}/set-primary", async (
                ISupplierDirectoryService s, int supplierId, int id, ApiSupplierLifecycleRequest r,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(ToApiDto(await s.SetPrimaryContactAsync(
                        supplierId, id, r?.ExpectedVersion ?? 0, ct)));
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
            }).WithName("SetPrimarySupplierContact")
            .WithApiCapability(PermissionResourceCatalog.SupplierContacts, PermissionAction.SetPrimary)
            .Produces<ApiSupplierContactDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
            endpoints.MapDelete("/api/suppliers/{supplierId:int}/contacts/{id:int}", async (ISupplierDirectoryService s,
                int supplierId, int id, int? expectedVersion, CancellationToken ct) =>
            {
                try
                {
                    return await s.DeleteContactAsync(supplierId, id, ct, expectedVersion ?? 0)
                        ? Results.Ok(new ApiCommandResponse(true, "联系人已删除。")) : Results.NotFound();
                }
                catch (BusinessConcurrencyException ex) { return Results.Conflict(new ApiErrorResponse(ex.Message)); }
            }).WithName("DeleteSupplierContact")
            .WithApiCapability(PermissionResourceCatalog.SupplierContacts, PermissionAction.Delete)
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
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
            x.Id, x.SupplierCompanyId, x.AssessmentDate, x.AssessmentKind, x.QualityScore, x.DeliveryScore,
            x.ServiceScore, x.PriceScore, x.AverageScore, x.Conclusion, x.Notes, x.AssessedBy,
            x.Status, x.ConfirmedBy, x.ConfirmedAt,
            x.CreatedAt, x.UpdatedAt, x.VersionNumber);
        private static ApiSupplierAssessmentOverviewDto ToApiDto(SupplierAssessmentOverview x) => new(
            x.TotalSuppliers, x.AssessedSuppliers, x.UnassessedSuppliers,
            x.PreferredCount, x.QualifiedCount, x.WatchCount, x.PausedCount,
            x.AverageQualityScore, x.AverageDeliveryScore, x.AverageServiceScore, x.AveragePriceScore,
            x.Items.Select(ToApiDto).ToArray());
        private static ApiSupplierAssessmentOverviewItemDto ToApiDto(SupplierAssessmentOverviewItem x) => new(
            x.SupplierCompanyId, x.SupplierName, x.SupplierStatus, x.Category, x.AssessmentCount,
            x.LatestAssessmentDate, x.LatestAssessmentKind, x.QualityScore, x.DeliveryScore,
            x.ServiceScore, x.PriceScore, x.AverageScore, x.Conclusion, x.Notes);

        private static SupplierSaveRequest ToSaveRequest(ApiSupplierSaveRequest x, int id) => new(id, x.Name,
            x.CountryRegion, x.Category, x.Website, x.MainProducts, x.Notes, x.ExpectedVersion);
        private static SupplierContactSaveRequest ToSaveRequest(ApiSupplierContactSaveRequest x, int supplierId, int id) =>
            new(id, supplierId, x.Name, x.Title, x.Email, x.Phone, x.InstantMessaging, x.ExpectedVersion);
        private static ApiSupplierImportPreviewDto ToApiDto(SupplierImportPreview x) => new(x.TotalRows, x.ValidRows, x.DuplicateRows, x.Rows.Select(ToApiDto).ToArray(), x.PreviewId);
        private static ApiSupplierImportRowDto ToApiDto(SupplierImportRow x) => new(x.RowNumber, x.Name, x.CountryRegion, x.Category, x.Website, x.Status, x.MainProducts, x.Notes, x.ContactName, x.ContactTitle, x.ContactEmail, x.ContactPhone, x.IsDuplicate, x.Error);
        private static SupplierProductLinkSaveRequest ToSaveRequest(ApiSupplierProductLinkSaveRequest x, int supplierCompanyId, int id) =>
            new(id, supplierCompanyId, x.ProductId, x.SupplierProductCode, x.ReferencePrice, x.Currency,
                x.LeadTimeDays, x.ExpectedVersion);
        private static SupplierAssessmentSaveRequest ToSaveRequest(ApiSupplierAssessmentSaveRequest x, int supplierCompanyId, int id) =>
            new(id, supplierCompanyId, x.AssessmentDate, x.AssessmentKind, x.QualityScore, x.DeliveryScore,
                x.ServiceScore, x.PriceScore, x.Conclusion, x.Notes, x.ExpectedVersion);
    }
}

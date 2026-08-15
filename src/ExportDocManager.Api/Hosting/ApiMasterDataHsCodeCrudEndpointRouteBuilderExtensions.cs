using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;


namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapHsCodeCrudEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/hs-codes", async (
                HttpContext context,
                IHsCodeReadRepository repository,
                int? pageNumber,
                int? pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var result = await repository.QueryPageAsync(
                    new HsCodeReadQuery
                    {
                        PageNumber = pageNumber ?? 1,
                        PageSize = Math.Min(Math.Max(pageSize ?? 50, 1), 200),
                        Keyword = keyword ?? string.Empty
                    },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromPagedHsCodes(result));
            })
            .WithName("ListHsCodes")
            .Produces<ApiPagedResponse<ApiHsCodeDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPost("/api/master-data/hs-codes", async (
                HttpContext context,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增HS编码不能包含已有ID。"));
                }

                if (string.IsNullOrWhiteSpace(request.Code))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码不能为空。"));
                }

                HsCode hsCode;
                try
                {
                    hsCode = ApiMasterDataDtoFactory.ToHsCodeForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("HS编码");
                }
                hsCode.Id = 0;
                hsCode.RowVersion = null;

                await hsCodeService.SaveAsync(hsCode);
                var saved = await repository.GetByCodeAsync(hsCode.Code, cancellationToken) ?? hsCode;
                return Results.Created(
                    $"/api/master-data/hs-codes/{saved.Code}",
                    ApiMasterDataDtoFactory.FromHsCode(saved));
            })
            .WithName("CreateHsCode")
            .Produces<ApiHsCodeDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapGet("/api/master-data/hs-codes/{code}", async (
                HttpContext context,
                IHsCodeReadRepository repository,
                string code,
                CancellationToken cancellationToken) =>
            {

                if (string.IsNullOrWhiteSpace(code))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码不能为空。"));
                }

                var row = await repository.GetByCodeAsync(code, cancellationToken);
                return row == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromHsCode(row));
            })
            .WithName("GetHsCode")
            .Produces<ApiHsCodeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapGet("/api/invoices/hs-codes/{code}", async (
                HttpContext context,
                IHsCodeReadRepository repository,
                string code,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(code))
                    return Results.BadRequest(new ApiErrorResponse("HS编码不能为空。"));
                var row = await repository.GetByCodeAsync(code, cancellationToken);
                return row == null ? Results.NotFound() : Results.Ok(ApiMasterDataDtoFactory.FromHsCode(row));
            }).WithName("GetInvoiceHsCode")
            .WithApiPermission(PermissionModuleCatalog.DocumentInvoices)
            .Produces<ApiHsCodeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

            endpoints.MapPut("/api/master-data/hs-codes/{code}", async (
                HttpContext context,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                string code,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {

                if (string.IsNullOrWhiteSpace(code))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码不能为空。"));
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码请求体不能为空。"));
                }

                var normalizedPathCode = HsCodeTextHelper.NormalizeCode(code);
                var normalizedRequestCode = HsCodeTextHelper.NormalizeCode(request.Code);
                if (!string.IsNullOrWhiteSpace(normalizedRequestCode) &&
                    !string.Equals(normalizedPathCode, normalizedRequestCode, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体HS编码与路径编码不一致。"));
                }

                var existing = await repository.GetByCodeAsync(code, cancellationToken);
                if (existing == null)
                {
                    return Results.NotFound();
                }

                HsCode hsCode;
                try
                {
                    hsCode = ApiMasterDataDtoFactory.ToHsCodeForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("HS编码");
                }
                hsCode.Id = existing.Id;
                hsCode.Code = normalizedPathCode;

                await hsCodeService.SaveAsync(hsCode);
                var saved = await repository.GetByCodeAsync(hsCode.Code, cancellationToken) ?? hsCode;
                return Results.Ok(ApiMasterDataDtoFactory.FromHsCode(saved));
            })
            .WithName("UpdateHsCode")
            .Produces<ApiHsCodeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapDelete("/api/master-data/hs-codes/by-id/{id:int}", async (
                HttpContext context,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("HS编码");
                }

                if (await FindHsCodeByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                await hsCodeService.DeleteAsync(id);
                return Results.Ok(new ApiCommandResponse(true, "HS编码已删除。"));
            })
            .WithName("DeleteHsCode")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/delete-batch", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeBatchDeleteRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanUseModule(
                        user,
                        PermissionModuleCatalog.DocumentHsKnowledge,
                        PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("只有管理权限可以批量删除HS编码。");
                }

                var ids = request?.Ids?
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList()
                    ?? new List<int>();
                if (ids.Count == 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("请先选择要删除的HS编码。"));
                }
                if (ids.Count > MaximumHsCodeBatchDeleteCount)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        $"单次最多删除 {MaximumHsCodeBatchDeleteCount:N0} 条HS编码；如需清空请使用独立的清空操作。"));
                }

                var existingIds = (await repository.FindExistingIdsAsync(ids, cancellationToken)).ToList();
                if (existingIds.Count == 0)
                {
                    return Results.NotFound();
                }

                await hsCodeService.DeleteAsync(existingIds);
                return Results.Ok(new ApiCommandResponse(
                    true,
                    $"已删除 {existingIds.Count} 条HS编码。"));
            })
            .WithName("DeleteHsCodesBatch")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/clear-all", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeClearAllRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!authorizationService.CanManageSettings(user))
                {
                    return WriteForbidden("只有管理员可以清空本地HS编码库。");
                }

                if (!string.Equals(request?.Confirmation?.Trim(), "CLEAR", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new ApiErrorResponse("清空本地HS编码库需要输入确认文本 CLEAR。"));
                }

                var before = await repository.QueryPageAsync(
                    new HsCodeReadQuery
                    {
                        PageNumber = 1,
                        PageSize = 1
                    },
                    cancellationToken);

                await hsCodeService.ClearAllLocalAsync();
                return Results.Ok(new ApiCommandResponse(
                    true,
                    before.TotalCount > 0
                        ? $"本地HS编码库已清空，共删除 {before.TotalCount} 条记录。"
                        : "本地HS编码库已为空。"));
            })
            .WithName("ClearAllHsCodes")
            .Produces<ApiCommandResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

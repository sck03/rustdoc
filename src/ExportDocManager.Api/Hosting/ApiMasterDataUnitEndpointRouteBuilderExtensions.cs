using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapUnitMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/units", async Task<Results<
                Ok<IReadOnlyList<ApiUnitDto>>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IUnitReadRepository repository,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                var rows = await repository.QueryAsync(
                    new UnitReadQuery
                    {
                        Keyword = keyword ?? string.Empty,
                        // Unit selectors are intentionally complete for normal use,
                        // but remain bounded so a corrupted or unexpectedly large
                        // catalog cannot materialize an unbounded response.
                        MaxCount = 500
                    },
                    cancellationToken);

                return TypedResults.Ok(ApiMasterDataDtoFactory.FromUnits(rows));
            })
            .WithName("ListUnits");

            endpoints.MapGet("/api/master-data/units/page", async Task<Results<
                Ok<ApiPagedResponse<ApiUnitDto>>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IUnitReadRepository repository,
                int pageNumber,
                int pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var page = await repository.QueryPageAsync(new UnitReadQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Keyword = keyword ?? string.Empty
                }, cancellationToken);
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPage(page, ApiMasterDataDtoFactory.FromUnits));
            })
            .WithName("ListUnitsPage");

            endpoints.MapGet("/api/master-data/units/{id:int}", async Task<Results<
                Ok<ApiUnitDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IUnitReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("单位");
                }

                var unit = await FindUnitByIdAsync(repository, id, cancellationToken);
                return unit == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiMasterDataDtoFactory.FromUnit(unit));
            })
            .WithName("GetUnit");

            endpoints.MapPost("/api/master-data/units", async Task<Results<
                Created<ApiUnitDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                ApiUnitDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("单位请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增单位不能包含已有ID。"));
                }

                Unit unit;
                try
                {
                    unit = ApiMasterDataDtoFactory.ToUnitForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("单位");
                }
                unit.Id = 0;
                unit.RowVersion = null;

                await auxiliaryService.SaveUnitAsync(unit, cancellationToken);
                return TypedResults.Created(
                    $"/api/master-data/units/{unit.Id}",
                    ApiMasterDataDtoFactory.FromUnit(unit));
            })
            .WithName("CreateUnit")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/master-data/units/{id:int}", async Task<Results<
                Ok<ApiUnitDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IUnitReadRepository repository,
                int id,
                ApiUnitDto request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("单位");
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("单位请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体单位ID与路径ID不一致。"));
                }

                if (await FindUnitByIdAsync(repository, id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                Unit unit;
                try
                {
                    unit = ApiMasterDataDtoFactory.ToUnitForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("单位");
                }
                unit.Id = id;

                await auxiliaryService.SaveUnitAsync(unit, cancellationToken);
                var saved = await FindUnitByIdAsync(repository, id, cancellationToken) ?? unit;
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromUnit(saved));
            })
            .WithName("UpdateUnit")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/master-data/units/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IUnitReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("单位");
                }

                if (await FindUnitByIdAsync(repository, id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                await auxiliaryService.DeleteUnitAsync(id, cancellationToken);
                return TypedResults.Ok(new ApiCommandResponse(true, "单位已删除。"));
            })
            .WithName("DeleteUnit")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}

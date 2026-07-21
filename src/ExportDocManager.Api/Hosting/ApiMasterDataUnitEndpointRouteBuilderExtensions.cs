using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapUnitMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/units", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IUnitReadRepository repository,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new UnitReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromUnits(rows));
            })
            .WithName("ListUnits");

            endpoints.MapGet("/api/master-data/units/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IUnitReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("单位");
                }

                var unit = await FindUnitByIdAsync(repository, id, cancellationToken);
                return unit == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromUnit(unit));
            })
            .WithName("GetUnit");

            endpoints.MapPost("/api/master-data/units", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                ApiUnitDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("单位请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增单位不能包含已有ID。"));
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

                try
                {
                    await auxiliaryService.SaveUnitAsync(unit);
                    return Results.Created(
                        $"/api/master-data/units/{unit.Id}",
                        ApiMasterDataDtoFactory.FromUnit(unit));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreateUnit");

            endpoints.MapPut("/api/master-data/units/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IUnitReadRepository repository,
                int id,
                ApiUnitDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("单位");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("单位请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体单位ID与路径ID不一致。"));
                }

                if (await FindUnitByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
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

                try
                {
                    await auxiliaryService.SaveUnitAsync(unit);
                    var saved = await FindUnitByIdAsync(repository, id, cancellationToken) ?? unit;
                    return Results.Ok(ApiMasterDataDtoFactory.FromUnit(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdateUnit");

            endpoints.MapDelete("/api/master-data/units/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IUnitReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("单位");
                }

                if (await FindUnitByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    await auxiliaryService.DeleteUnitAsync(id);
                    return Results.Ok(new ApiCommandResponse(true, "单位已删除。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteUnit");
        }
    }
}

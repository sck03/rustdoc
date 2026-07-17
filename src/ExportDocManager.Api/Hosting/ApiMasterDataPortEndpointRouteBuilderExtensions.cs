using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPortMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/ports", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPortReadRepository repository,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new PortReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromPorts(rows));
            })
            .WithName("ListPorts");

            endpoints.MapGet("/api/master-data/ports/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPortReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("港口");
                }

                var port = await FindPortByIdAsync(repository, id, cancellationToken);
                return port == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromPort(port));
            })
            .WithName("GetPort");

            endpoints.MapPost("/api/master-data/ports", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                ApiPortDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("港口请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增港口不能包含已有ID。"));
                }

                var port = ApiMasterDataDtoFactory.ToPortForSave(request);
                port.Id = 0;

                try
                {
                    await auxiliaryService.SavePortAsync(port);
                    return Results.Created(
                        $"/api/master-data/ports/{port.Id}",
                        ApiMasterDataDtoFactory.FromPort(port));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreatePort");

            endpoints.MapPut("/api/master-data/ports/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IPortReadRepository repository,
                int id,
                ApiPortDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("港口");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("港口请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体港口ID与路径ID不一致。"));
                }

                if (await FindPortByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                var port = ApiMasterDataDtoFactory.ToPortForSave(request);
                port.Id = id;

                try
                {
                    await auxiliaryService.SavePortAsync(port);
                    var saved = await FindPortByIdAsync(repository, id, cancellationToken) ?? port;
                    return Results.Ok(ApiMasterDataDtoFactory.FromPort(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdatePort");

            endpoints.MapDelete("/api/master-data/ports/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IPortReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("港口");
                }

                if (await FindPortByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    await auxiliaryService.DeletePortAsync(id);
                    return Results.Ok(new ApiCommandResponse(true, "港口已删除。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeletePort");
        }
    }
}

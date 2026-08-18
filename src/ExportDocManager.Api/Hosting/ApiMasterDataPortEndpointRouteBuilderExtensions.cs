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
        private static void MapPortMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/ports", async Task<Results<
                Ok<IReadOnlyList<ApiPortDto>>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPortReadRepository repository,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                var rows = await repository.QueryAsync(
                    new PortReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPorts(rows));
            })
            .WithName("ListPorts");

            endpoints.MapGet("/api/master-data/ports/page", async Task<Results<
                Ok<ApiPagedResponse<ApiPortDto>>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPortReadRepository repository,
                int pageNumber,
                int pageSize,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var page = await repository.QueryPageAsync(new PortReadQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Keyword = keyword ?? string.Empty
                }, cancellationToken);
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPage(page, ApiMasterDataDtoFactory.FromPorts));
            })
            .WithName("ListPortsPage");

            endpoints.MapGet("/api/master-data/ports/{id:int}", async Task<Results<
                Ok<ApiPortDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IPortReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("港口");
                }

                var port = await FindPortByIdAsync(repository, id, cancellationToken);
                return port == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(ApiMasterDataDtoFactory.FromPort(port));
            })
            .WithName("GetPort");

            endpoints.MapPost("/api/master-data/ports", async Task<Results<
                Created<ApiPortDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                ApiPortDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("港口请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("新增港口不能包含已有ID。"));
                }

                Port port;
                try
                {
                    port = ApiMasterDataDtoFactory.ToPortForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("港口");
                }
                port.Id = 0;
                port.RowVersion = null;

                await auxiliaryService.SavePortAsync(port, cancellationToken);
                return TypedResults.Created(
                    $"/api/master-data/ports/{port.Id}",
                    ApiMasterDataDtoFactory.FromPort(port));
            })
            .WithName("CreatePort")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapPut("/api/master-data/ports/{id:int}", async Task<Results<
                Ok<ApiPortDto>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IPortReadRepository repository,
                int id,
                ApiPortDto request,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("港口");
                }

                if (request == null)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("港口请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return TypedResults.BadRequest(new ApiErrorResponse("请求体港口ID与路径ID不一致。"));
                }

                if (await FindPortByIdAsync(repository, id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                Port port;
                try
                {
                    port = ApiMasterDataDtoFactory.ToPortForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("港口");
                }
                port.Id = id;

                await auxiliaryService.SavePortAsync(port, cancellationToken);
                var saved = await FindPortByIdAsync(repository, id, cancellationToken) ?? port;
                return TypedResults.Ok(ApiMasterDataDtoFactory.FromPort(saved));
            })
            .WithName("UpdatePort")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/master-data/ports/{id:int}", async Task<Results<
                Ok<ApiCommandResponse>,
                BadRequest<ApiErrorResponse>,
                UnauthorizedHttpResult,
                NotFound>> (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAuxiliaryService auxiliaryService,
                IPortReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {

                if (id <= 0)
                {
                    return BadMasterDataId("港口");
                }

                if (await FindPortByIdAsync(repository, id, cancellationToken) == null)
                {
                    return TypedResults.NotFound();
                }

                await auxiliaryService.DeletePortAsync(id, cancellationToken);
                return TypedResults.Ok(new ApiCommandResponse(true, "港口已删除。"));
            })
            .WithName("DeletePort")
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }
    }
}

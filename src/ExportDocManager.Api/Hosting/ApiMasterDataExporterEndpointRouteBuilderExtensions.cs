using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExporterMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/exporters", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterReadRepository repository,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var rows = await repository.QueryAsync(
                    new ExporterReadQuery { Keyword = keyword ?? string.Empty },
                    cancellationToken);

                return Results.Ok(ApiMasterDataDtoFactory.FromExporters(rows));
            })
            .WithName("ListExporters");

            endpoints.MapGet("/api/master-data/exporters/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                var exporter = await exporterService.GetExporterByIdAsync(id);
                return exporter == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromExporter(exporter));
            })
            .WithName("GetExporter");

            endpoints.MapPost("/api/master-data/exporters", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                ApiExporterDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("出口商请求体不能为空。"));
                }

                if (request.Id > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse("新增出口商不能包含已有ID。"));
                }

                Exporter exporter;
                try
                {
                    exporter = ApiMasterDataDtoFactory.ToExporterForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("出口商");
                }

                exporter.Id = 0;
                exporter.RowVersion = null;

                try
                {
                    int savedId = await exporterService.SaveExporterAsync(exporter);
                    var saved = await exporterService.GetExporterByIdAsync(savedId) ?? exporter;
                    return Results.Created(
                        $"/api/master-data/exporters/{savedId}",
                        ApiMasterDataDtoFactory.FromExporter(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreateExporter");

            endpoints.MapPut("/api/master-data/exporters/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id,
                ApiExporterDto request) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("出口商请求体不能为空。"));
                }

                if (request.Id > 0 && request.Id != id)
                {
                    return Results.BadRequest(new ApiErrorResponse("请求体出口商ID与路径ID不一致。"));
                }

                if (await exporterService.GetExporterByIdAsync(id) == null)
                {
                    return Results.NotFound();
                }

                Exporter exporter;
                try
                {
                    exporter = ApiMasterDataDtoFactory.ToExporterForSave(request);
                }
                catch (FormatException)
                {
                    return BadRowVersion("出口商");
                }

                exporter.Id = id;

                try
                {
                    int savedId = await exporterService.SaveExporterAsync(exporter);
                    var saved = await exporterService.GetExporterByIdAsync(savedId) ?? exporter;
                    return Results.Ok(ApiMasterDataDtoFactory.FromExporter(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdateExporter");

            endpoints.MapDelete("/api/master-data/exporters/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IExporterService exporterService,
                int id) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("出口商");
                }

                if (await exporterService.GetExporterByIdAsync(id) == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    await exporterService.DeleteExporterAsync(id);
                    return Results.Ok(new ApiCommandResponse(true, "出口商已删除。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteExporter");
        }
    }
}

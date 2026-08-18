using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Time;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;


namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapHsCodeRemoteEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/hs-codes/remote-health", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                CancellationToken cancellationToken) =>
            {
                var health = await hsCodeService.GetRemoteSourceHealthAsync(cancellationToken);
                return Results.Ok(new ApiHsCodeRemoteHealthResponse(health.Source, health.Available, health.CheckedAt, health.Message));
            }).WithName("GetHsCodeRemoteHealth")
            .Produces<ApiHsCodeRemoteHealthResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapGet("/api/master-data/hs-codes/search-remote", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                string? keyword,
                CancellationToken cancellationToken) =>
            {

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码远程查询关键字不能为空。"));
                }

                try
                {
                    var evidence = await hsCodeService.SearchRemoteEvidenceAsync(keyword.Trim(), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var standardRecords = evidence.Records
                        .Where(record => record.Kind == HsCodeRemoteRecordKind.StandardCode && !record.IsExpired)
                        .GroupBy(record => HsCodeTextHelper.NormalizeCode(record.Item.Code), StringComparer.OrdinalIgnoreCase)
                        .Select(group => group
                            .OrderByDescending(record => record.InstanceCount.HasValue)
                            .ThenByDescending(record => !string.IsNullOrWhiteSpace(record.Item.Description))
                            .First())
                        .ToList();
                    var items = standardRecords.Select(ApiMasterDataDtoFactory.FromRemoteRecord).ToList();
                    return Results.Ok(new ApiHsCodeSearchResponse(
                        items,
                        items.Count,
                        "remote",
                        "远程HS编码查询只读取在线来源；不会写入候选池。需要进入待审核候选池时，请使用“查询并加入候选”。",
                        standardRecords.Count,
                        evidence.Records.Count(record => record.Kind == HsCodeRemoteRecordKind.DeclarationExample)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            })
            .WithName("SearchRemoteHsCodes")
            .Produces<ApiHsCodeSearchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/search-remote/capture", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                ApiHsCodeRemoteSearchRequest request,
                CancellationToken cancellationToken) =>
            {

                if (request == null || string.IsNullOrWhiteSpace(request.Keyword))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码远程查询关键字不能为空。"));
                }

                try
                {
                    string keyword = request.Keyword.Trim();
                    var evidence = await hsCodeService.SearchRemoteEvidenceAsync(keyword, cancellationToken);
                    await knowledgeService.CaptureRemoteEvidenceAsync(keyword, evidence, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var standardRecords = evidence.Records
                        .Where(record => record.Kind == HsCodeRemoteRecordKind.StandardCode && !record.IsExpired)
                        .GroupBy(record => HsCodeTextHelper.NormalizeCode(record.Item.Code), StringComparer.OrdinalIgnoreCase)
                        .Select(group => group
                            .OrderByDescending(record => record.InstanceCount.HasValue)
                            .ThenByDescending(record => !string.IsNullOrWhiteSpace(record.Item.Description))
                            .First())
                        .ToList();
                    var items = standardRecords.Select(ApiMasterDataDtoFactory.FromRemoteRecord).ToList();
                    return Results.Ok(new ApiHsCodeSearchResponse(
                        items,
                        items.Count,
                        "remote",
                        "联网查询结果已返回，申报实例已进入待审核候选池；确认后才会进入正式共享实例库。",
                        standardRecords.Count,
                        evidence.Records.Count(record => record.Kind == HsCodeRemoteRecordKind.DeclarationExample)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            })
            .WithName("CaptureRemoteHsCodes")
            .Produces<ApiHsCodeSearchResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/fetch-remote-detail", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码详情请求体不能为空。"));
                }

                if (string.IsNullOrWhiteSpace(request.DetailUrl))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码远程详情地址不能为空。"));
                }

                try
                {
                    var hsCode = ApiMasterDataDtoFactory.ToHsCodeForSave(request);
                    var detailed = await hsCodeService.FetchDetailAsync(hsCode, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Results.Ok(ApiMasterDataDtoFactory.FromHsCode(detailed));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("FetchRemoteHsCodeDetail")
            .Produces<ApiHsCodeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

            endpoints.MapPost("/api/master-data/hs-codes/resolve-remote-detail", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                ApiHsCodeDto request,
                IBusinessClock clock,
                CancellationToken cancellationToken) =>
            {

                if (request == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码详情请求体不能为空。"));
                }

                if (string.IsNullOrWhiteSpace(request.DetailUrl))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码远程详情地址不能为空。"));
                }

                try
                {
                    var response = await ResolveRemoteHsCodeDetailAsync(hsCodeService, knowledgeService, request, clock, cancellationToken);
                    return Results.Ok(response);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidOperationException ex)
                {
                    return WriteServiceException(ex);
                }
            })
            .WithName("ResolveRemoteHsCodeDetail")
            .Produces<ApiHsCodeRemoteDetailResolutionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

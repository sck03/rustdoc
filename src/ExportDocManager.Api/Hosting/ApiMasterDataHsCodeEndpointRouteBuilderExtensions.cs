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
        private static void MapHsCodeMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/master-data/hs-codes/import-preview-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IAppPathProvider pathProvider,
                ApiHsCodeImportPreviewPathRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入文件路径不能为空。"));
                if (!File.Exists(request.FilePath)) return Results.NotFound(new ApiErrorResponse("HS编码导入文件不存在。"));
                if (!IsAllowedHsCodeImportFileName(request.FilePath))
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入仅支持 .xlsx 或 .xlsm 文件。"));
                try
                {
                    var preview = await hsCodeService.PreviewImportAsync(
                        request.FilePath,
                        ParseHsCodeImportMode(request.Mode),
                        request.SourceName,
                        request.EffectiveYear,
                        cancellationToken);
                    return Results.Ok(await StoreHsCodeImportPreviewAsync(pathProvider, preview, cancellationToken));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteConflict(ex.Message);
                }
            }).WithName("PreviewHsCodesImportFromPath");

            endpoints.MapPost("/api/master-data/hs-codes/import-preview-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(pathProvider, "HsCodeImports", "hs-preview");
                try
                {
                    string fileName = NormalizeUploadedHsCodeImportFileName(context.Request.Query["fileName"].ToString());
                    string importPath = Path.Combine(tempRoot, fileName);
                    await using (var output = File.Create(importPath))
                    {
                        await context.Request.Body.CopyToAsync(output, cancellationToken);
                    }
                    if (new FileInfo(importPath).Length == 0) return Results.BadRequest(new ApiErrorResponse("HS编码导入文件不能为空。"));
                    var preview = await hsCodeService.PreviewImportAsync(
                        importPath,
                        ParseHsCodeImportMode(context.Request.Query["mode"].ToString()),
                        context.Request.Query["sourceName"].ToString(),
                        int.TryParse(context.Request.Query["effectiveYear"], out int year) ? year : null,
                        cancellationToken);
                    return Results.Ok(await StoreHsCodeImportPreviewAsync(pathProvider, preview, cancellationToken));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteConflict(ex.Message);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            }).WithName("PreviewHsCodesImportUpload");

            endpoints.MapPost("/api/master-data/hs-codes/import-commit", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                IAppPathProvider pathProvider,
                ApiHsCodeImportCommitRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                if (request == null || !Guid.TryParseExact(request.Token, "N", out _))
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入预检令牌无效。"));
                string previewPath = GetHsCodeImportPreviewPath(pathProvider, request.Token);
                if (!File.Exists(previewPath)) return Results.NotFound(new ApiErrorResponse("导入预检已过期，请重新选择文件。"));
                try
                {
                    await using var input = File.OpenRead(previewPath);
                    var preview = await System.Text.Json.JsonSerializer.DeserializeAsync<HsCodeImportPreview>(input, cancellationToken: cancellationToken)
                        ?? throw new InvalidDataException("HS编码导入预检内容无效。");
                    var result = await hsCodeService.CommitImportAsync(preview, cancellationToken);
                    await knowledgeService.RefreshReplacementRelationsAsync(preview, cancellationToken);
                    return Results.Ok(new ApiHsCodeImportCommitResponse(
                        true, result.AddedCount, result.UpdatedCount, result.UnchangedCount,
                        result.SuspectedObsoleteCount, result.SkippedCount, result.Message));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteConflict(ex.Message);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteFile(previewPath);
                }
            }).WithName("CommitHsCodesImport");

            endpoints.MapGet("/api/master-data/hs-codes/remote-health", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                var health = await hsCodeService.GetRemoteSourceHealthAsync(cancellationToken);
                return Results.Ok(new ApiHsCodeRemoteHealthResponse(health.Source, health.Available, health.CheckedAt, health.Message));
            }).WithName("GetHsCodeRemoteHealth");

            endpoints.MapGet("/api/master-data/hs-codes", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeReadRepository repository,
                int? pageNumber,
                int? pageSize,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
            .WithName("ListHsCodes");

            endpoints.MapPost("/api/master-data/hs-codes/import-path", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeImportPathRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入文件路径不能为空。"));
                }

                string filePath = request.FilePath.Trim();
                if (!File.Exists(filePath))
                {
                    return Results.NotFound(new ApiErrorResponse("HS编码导入文件不存在。"));
                }

                if (!IsAllowedHsCodeImportFileName(filePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码导入仅支持 .xlsx 或 .xlsm 文件。"));
                }

                try
                {
                    await hsCodeService.ImportAsync(filePath);
                    return Results.Ok(await BuildHsCodeImportResponseAsync(repository, filePath, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("ImportHsCodesFromPath");

            endpoints.MapPost("/api/master-data/hs-codes/import-upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "HsCodeImports",
                    "hs-upload");

                try
                {
                    string fileName = NormalizeUploadedHsCodeImportFileName(
                        context.Request.Query["fileName"].ToString());
                    string importPath = Path.Combine(tempRoot, fileName);
                    await using (var output = File.Create(importPath))
                    {
                        await context.Request.Body.CopyToAsync(output, cancellationToken);
                    }

                    if (new FileInfo(importPath).Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("HS编码导入文件不能为空。"));
                    }

                    await hsCodeService.ImportAsync(importPath);
                    return Results.Ok(await BuildHsCodeImportResponseAsync(repository, fileName, cancellationToken));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (InvalidDataException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                catch (IOException ex)
                {
                    return WriteConflict(ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return WriteConflict(ex.Message);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            })
            .WithName("UploadHsCodesImportFile");

            endpoints.MapGet("/api/master-data/hs-codes/search-remote", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                string keyword,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码远程查询关键字不能为空。"));
                }

                try
                {
                    var evidence = await hsCodeService.SearchRemoteEvidenceAsync(keyword.Trim(), cancellationToken);
                    await knowledgeService.CaptureRemoteEvidenceAsync(keyword.Trim(), evidence, cancellationToken);
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
                        "远程HS编码查询只读取在线来源；标准编码与申报实例分开显示，确认保存时才写当前运行数据根数据库。",
                        standardRecords.Count,
                        evidence.Records.Count(record => record.Kind == HsCodeRemoteRecordKind.DeclarationExample)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("SearchRemoteHsCodes");

            endpoints.MapPost("/api/master-data/hs-codes/fetch-remote-detail", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
                    return WriteConflict(ex.Message);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("FetchRemoteHsCodeDetail");

            endpoints.MapPost("/api/master-data/hs-codes/resolve-remote-detail", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeKnowledgeService knowledgeService,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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
                    var response = await ResolveRemoteHsCodeDetailAsync(hsCodeService, knowledgeService, request, cancellationToken);
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
                    return WriteConflict(ex.Message);
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("ResolveRemoteHsCodeDetail");

            endpoints.MapPost("/api/master-data/hs-codes", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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

                var hsCode = ApiMasterDataDtoFactory.ToHsCodeForSave(request);
                hsCode.Id = 0;

                try
                {
                    await hsCodeService.SaveAsync(hsCode);
                    var saved = await repository.GetByCodeAsync(hsCode.Code, cancellationToken) ?? hsCode;
                    return Results.Created(
                        $"/api/master-data/hs-codes/{saved.Code}",
                        ApiMasterDataDtoFactory.FromHsCode(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("CreateHsCode");

            endpoints.MapGet("/api/master-data/hs-codes/{code}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeReadRepository repository,
                string code,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    return Results.BadRequest(new ApiErrorResponse("HS编码不能为空。"));
                }

                var row = await repository.GetByCodeAsync(code, cancellationToken);
                return row == null
                    ? Results.NotFound()
                    : Results.Ok(ApiMasterDataDtoFactory.FromHsCode(row));
            })
            .WithName("GetHsCode");

            endpoints.MapGet("/api/invoices/hs-codes/{code}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeReadRepository repository,
                string code,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(code))
                    return Results.BadRequest(new ApiErrorResponse("HS编码不能为空。"));
                var row = await repository.GetByCodeAsync(code, cancellationToken);
                return row == null ? Results.NotFound() : Results.Ok(ApiMasterDataDtoFactory.FromHsCode(row));
            }).WithName("GetInvoiceHsCode");

            endpoints.MapPut("/api/master-data/hs-codes/{code}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                string code,
                ApiHsCodeDto request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

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

                var hsCode = ApiMasterDataDtoFactory.ToHsCodeForSave(request);
                hsCode.Id = existing.Id;
                hsCode.Code = normalizedPathCode;

                try
                {
                    await hsCodeService.SaveAsync(hsCode);
                    var saved = await repository.GetByCodeAsync(hsCode.Code, cancellationToken) ?? hsCode;
                    return Results.Ok(ApiMasterDataDtoFactory.FromHsCode(saved));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("UpdateHsCode");

            endpoints.MapDelete("/api/master-data/hs-codes/by-id/{id:int}", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                int id,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (id <= 0)
                {
                    return BadMasterDataId("HS编码");
                }

                if (await FindHsCodeByIdAsync(repository, id, cancellationToken) == null)
                {
                    return Results.NotFound();
                }

                try
                {
                    await hsCodeService.DeleteAsync(id);
                    return Results.Ok(new ApiCommandResponse(true, "HS编码已删除。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteHsCode");

            endpoints.MapPost("/api/master-data/hs-codes/delete-batch", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeBatchDeleteRequest request,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
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

                var rows = await repository.QueryAsync(
                    new HsCodeReadQuery { ReturnAll = true },
                    cancellationToken);
                var existingIds = rows
                    .Where(row => ids.Contains(row.Id))
                    .Select(row => row.Id)
                    .Distinct()
                    .ToList();
                if (existingIds.Count == 0)
                {
                    return Results.NotFound();
                }

                try
                {
                    await hsCodeService.DeleteAsync(existingIds);
                    return Results.Ok(new ApiCommandResponse(
                        true,
                        $"已删除 {existingIds.Count} 条HS编码。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("DeleteHsCodesBatch");

            endpoints.MapPost("/api/master-data/hs-codes/clear-all", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IHsCodeService hsCodeService,
                IHsCodeReadRepository repository,
                ApiHsCodeClearAllRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

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
                        PageSize = 1,
                        ReturnAll = true
                    },
                    cancellationToken);

                try
                {
                    await hsCodeService.ClearAllLocalAsync();
                    return Results.Ok(new ApiCommandResponse(
                        true,
                        before.TotalCount > 0
                            ? $"本地HS编码库已清空，共删除 {before.TotalCount} 条记录。"
                            : "本地HS编码库已为空。"));
                }
                catch (Exception ex)
                {
                    return WriteConflict(ex.Message);
                }
            })
            .WithName("ClearAllHsCodes");

            endpoints.MapGet("/api/master-data/hs-knowledge/search", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string query, int? maxResults, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return Results.Ok(await service.SearchAsync(query, maxResults ?? 20, cancellationToken));
            }).WithName("SearchHsCodeKnowledge");

            endpoints.MapGet("/api/invoices/hs-knowledge/search", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string query, int? maxResults, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return Results.Ok(await service.SearchAsync(query, maxResults ?? 20, cancellationToken));
            }).WithName("SearchInvoiceHsCodeKnowledge");

            endpoints.MapGet("/api/master-data/hs-knowledge/examples", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                int page = Math.Max(pageNumber ?? 1, 1);
                int size = Math.Clamp(pageSize ?? 50, 1, 200);
                var items = await service.ListExamplesAsync(keyword, page, size, cancellationToken);
                int total = await service.CountExamplesAsync(keyword, cancellationToken);
                return Results.Ok(new { items, totalCount = total, pageNumber = page, pageSize = size });
            }).WithName("ListHsCodeKnowledgeExamples");

            endpoints.MapPost("/api/master-data/hs-knowledge/examples", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeExampleInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { return Results.Ok(await service.SaveExampleAsync(request, cancellationToken)); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("SaveHsCodeKnowledgeExample");

            endpoints.MapDelete("/api/master-data/hs-knowledge/examples/{id:int}", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                int id, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return await service.DeleteExampleAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
            }).WithName("DeleteHsCodeKnowledgeExample");

            endpoints.MapPost("/api/master-data/hs-knowledge/examples/delete-batch", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IHsCodeKnowledgeService service,
                HsCodeKnowledgeExampleDeleteBatchInput request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null) return Results.Unauthorized();
                if (!authorizationService.CanUseModule(user, PermissionModuleCatalog.DocumentMasterData, PermissionAccessLevel.Manage))
                    return WriteForbidden("只有管理权限可以批量删除申报实例。");
                int deleted = await service.DeleteExamplesAsync(request?.Ids ?? [], cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已删除 {deleted} 条申报实例。"));
            }).WithName("DeleteHsCodeKnowledgeExamplesBatch");

            endpoints.MapPost("/api/master-data/hs-knowledge/feedback", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeKnowledgeFeedbackInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { await service.RecordFeedbackAsync(request, cancellationToken); return Results.Ok(new ApiCommandResponse(true, "已记录本次选择，本地推荐会逐步优化。")); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RecordHsCodeKnowledgeFeedback");

            endpoints.MapPost("/api/invoices/hs-knowledge/feedback", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeKnowledgeFeedbackInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { await service.RecordFeedbackAsync(request, cancellationToken); return Results.Ok(new ApiCommandResponse(true, "已记录本次发票归类选择。")); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("RecordInvoiceHsCodeKnowledgeFeedback");

            endpoints.MapGet("/api/master-data/hs-knowledge/history-candidates", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string keyword, int? maxResults, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return Results.Ok(await service.DiscoverHistoryCandidatesAsync(keyword, maxResults ?? 200, cancellationToken));
            }).WithName("DiscoverHsCodeHistoryCandidates");

            endpoints.MapGet("/api/master-data/hs-knowledge/remote-candidates", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                string status, string keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                return Results.Ok(await service.ListRemoteCandidatesAsync(
                    status,
                    keyword,
                    pageNumber ?? 1,
                    pageSize ?? 30,
                    cancellationToken));
            }).WithName("ListHsCodeRemoteCandidates");

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/review", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateReviewInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try { return await service.ReviewRemoteCandidateAsync(request, cancellationToken) ? Results.Ok(new ApiCommandResponse(true, request.Confirmed ? "已确认并加入正式申报实例库。" : "已忽略该联网候选。")) : Results.NotFound(); }
                catch (InvalidOperationException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("ReviewHsCodeRemoteCandidate");

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/review-batch", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateBatchReviewInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                try
                {
                    int reviewed = await service.ReviewRemoteCandidatesAsync(request?.Items ?? [], cancellationToken);
                    return Results.Ok(new ApiCommandResponse(true, $"已处理 {reviewed} 条联网候选。"));
                }
                catch (InvalidOperationException ex) { return Results.BadRequest(new ApiErrorResponse(ex.Message)); }
            }).WithName("ReviewHsCodeRemoteCandidatesBatch");

            endpoints.MapPost("/api/master-data/hs-knowledge/remote-candidates/reset", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                HsCodeRemoteCandidateResetInput request, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                int reset = await service.ResetRemoteCandidatesAsync(request?.Ids ?? [], cancellationToken);
                return Results.Ok(new ApiCommandResponse(true, $"已将 {reset} 条审核记录恢复为待审核。"));
            }).WithName("ResetHsCodeRemoteCandidates");

            endpoints.MapGet("/api/master-data/hs-knowledge/export", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                DateTimeOffset? since, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                byte[] package = await service.ExportPackageAsync(since, cancellationToken);
                return Results.File(package, "application/vnd.exportdocmanager.hs-knowledge+zip", $"ExportDocManager-HsLibrary-{DateTime.Now:yyyyMMdd}.edmhs");
            }).WithName("ExportHsCodeKnowledge");

            endpoints.MapPost("/api/master-data/hs-knowledge/import", async (
                HttpContext context, IApiSessionTokenService tokenService, IHsCodeKnowledgeService service,
                IAppPathProvider pathProvider, CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null) return Results.Unauthorized();
                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(pathProvider, "HsKnowledgeImports", "knowledge-import");
                try
                {
                    string path = Path.Combine(tempRoot, "library.edmhs");
                    await using (var output = File.Create(path)) await context.Request.Body.CopyToAsync(output, cancellationToken);
                    var preview = await service.PreviewPackageAsync(path, cancellationToken);
                    var result = await service.ImportPackageAsync(preview, cancellationToken);
                    return Results.Ok(new { preview.FileName, preview.HsCodeCount, preview.ExampleCount, preview.ReplacementCount, preview.FeedbackCount, preview.Warnings, result });
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
                {
                    return Results.BadRequest(new ApiErrorResponse(ex.Message));
                }
                finally { AtomicFileHelper.TryDeleteDirectory(tempRoot); }
            }).WithName("ImportHsCodeKnowledge");
        }

        private const string RemoteHsCodeDetailResolutionStoragePolicy =
            "HS编码联网详情补全只访问在线来源并沉淀待审核申报实例；第三方标准编码不会自动写成当前年度有效税则，过期编码只从本次结果中清理，不新增默认目录或系统 C 盘落点。";

        private static HsCodeImportMode ParseHsCodeImportMode(string value) =>
            string.Equals(value?.Trim(), "CompleteSnapshot", StringComparison.OrdinalIgnoreCase)
                ? HsCodeImportMode.CompleteSnapshot
                : HsCodeImportMode.Incremental;

        private static async Task<ApiHsCodeImportPreviewResponse> StoreHsCodeImportPreviewAsync(
            IAppPathProvider pathProvider,
            HsCodeImportPreview preview,
            CancellationToken cancellationToken)
        {
            string token = Guid.NewGuid().ToString("N");
            string path = GetHsCodeImportPreviewPath(pathProvider, token);
            string previewRoot = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(previewRoot);
            foreach (string staleFile in Directory.EnumerateFiles(previewRoot, "*.json")
                         .Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-24)))
            {
                AtomicFileHelper.TryDeleteFile(staleFile);
            }
            await using (var output = File.Create(path))
            {
                await System.Text.Json.JsonSerializer.SerializeAsync(output, preview, cancellationToken: cancellationToken);
            }
            return new ApiHsCodeImportPreviewResponse(
                token, preview.FileName, preview.Mode.ToString(), preview.SourceName, preview.EffectiveYear,
                preview.WorksheetName, preview.HeaderRowNumber, preview.Confidence,
                preview.Columns.Select(item => new ApiHsCodeImportColumnMappingDto(item.Field, item.Header, item.ColumnNumber, item.Confidence)).ToList(),
                preview.Items.Take(200).Select(item => new ApiHsCodeImportPreviewItemDto(
                    item.ChangeType, item.RowNumber, ApiMasterDataDtoFactory.FromHsCode(item.Item),
                    item.ChangedFields, item.ReplacementCandidates, item.Message)).ToList(),
                preview.AddCount, preview.UpdateCount, preview.UnchangedCount, preview.SuspectedObsoleteCount,
                preview.ConflictCount, preview.InvalidCount, preview.Warnings,
                "预检文件仅保存在运行数据根 Cache/HsCodeImports/Previews，提交或过期后删除；不会写系统临时目录，也不会触碰商业发票Excel导入。" );
        }

        private static string GetHsCodeImportPreviewPath(IAppPathProvider pathProvider, string token) =>
            Path.Combine(pathProvider.CacheRoot, "HsCodeImports", "Previews", $"{token}.json");

        private static async Task<ApiHsCodeRemoteDetailResolutionResponse> ResolveRemoteHsCodeDetailAsync(
            IHsCodeService hsCodeService,
            IHsCodeKnowledgeService knowledgeService,
            ApiHsCodeDto request,
            CancellationToken cancellationToken)
        {
            var seed = ApiMasterDataDtoFactory.ToHsCodeForSave(request);
            var recordKind = Enum.TryParse<HsCodeRemoteRecordKind>(request.RemoteRecordKind, true, out var parsedKind)
                ? parsedKind
                : HsCodeRemoteRecordKind.StandardCode;
            var record = new HsCodeRemoteSearchRecord(
                seed,
                recordKind,
                string.Equals(seed.Status, "Obsolete", StringComparison.OrdinalIgnoreCase),
                request.InstanceCount,
                request.SummaryUrl ?? string.Empty,
                string.IsNullOrWhiteSpace(request.EvidenceUrl) ? request.DetailUrl ?? string.Empty : request.EvidenceUrl,
                request.ObservedAt.HasValue ? new DateTimeOffset(request.ObservedAt.Value) : DateTimeOffset.UtcNow);
            var evidence = await hsCodeService.FetchRemoteDetailEvidenceAsync(record, cancellationToken);
            await knowledgeService.CaptureRemoteDetailEvidenceAsync(request.Name, evidence, cancellationToken);

            if (!evidence.IsExpired)
            {
                return new ApiHsCodeRemoteDetailResolutionResponse(
                    [ApiMasterDataDtoFactory.FromRemoteDetail(evidence)],
                    [],
                    1,
                    0,
                    evidence.DeclarationExamples.Count > 0
                        ? $"已补全HS详情，并提取 {evidence.DeclarationExamples.Count} 条待审核申报实例。"
                        : "已补全HS编码详情。",
                    RemoteHsCodeDetailResolutionStoragePolicy);
            }

            var replacementItems = new List<ApiHsCodeDto>();
            foreach (string recommendedKeyword in evidence.RecommendedKeywords.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var replacementSearch = await hsCodeService.SearchRemoteEvidenceAsync(recommendedKeyword, cancellationToken);
                await knowledgeService.CaptureRemoteEvidenceAsync(request.Name, replacementSearch, cancellationToken);
                foreach (var replacementRecord in replacementSearch.Records
                             .Where(item => item.Kind == HsCodeRemoteRecordKind.StandardCode && !item.IsExpired)
                             .GroupBy(item => HsCodeTextHelper.NormalizeCode(item.Item.Code), StringComparer.OrdinalIgnoreCase)
                             .Select(group => group.First()))
                {
                    var replacementDetail = await hsCodeService.FetchRemoteDetailEvidenceAsync(replacementRecord, cancellationToken);
                    await knowledgeService.CaptureRemoteDetailEvidenceAsync(request.Name, replacementDetail, cancellationToken);
                    if (replacementDetail.IsExpired) continue;
                    replacementItems.Add(ApiMasterDataDtoFactory.FromRemoteDetail(replacementDetail));
                }
                if (replacementItems.Count > 0) break;
            }

            return new ApiHsCodeRemoteDetailResolutionResponse(
                replacementItems,
                [ApiMasterDataDtoFactory.FromRemoteRecord(record)],
                replacementItems.Count,
                1,
                replacementItems.Count > 0
                    ? $"原编码已作废，已按网页推荐链补入 {replacementItems.Count} 条当前编码候选。"
                    : "原编码已作废，暂未在当前来源找到可验证的替代编码。",
                RemoteHsCodeDetailResolutionStoragePolicy);

        }

        private static async Task<ApiHsCodeImportResponse> BuildHsCodeImportResponseAsync(
            IHsCodeReadRepository repository,
            string filePathOrName,
            CancellationToken cancellationToken)
        {
            var page = await repository.QueryPageAsync(
                new HsCodeReadQuery
                {
                    PageNumber = 1,
                    PageSize = 1,
                    ReturnAll = true
                },
                cancellationToken);

            string fileName = Path.GetFileName(filePathOrName);
            return new ApiHsCodeImportResponse(
                true,
                string.IsNullOrWhiteSpace(fileName) ? "HS编码导入文件" : fileName,
                page.TotalCount,
                "HS编码已导入本地库。",
                "HS编码导入只读取用户显式选择或上传的 Excel 文件；本地库记录写入当前运行数据根数据库，上传临时文件使用运行数据根 Cache/HsCodeImports 并在请求结束后清理。");
        }

        private static string NormalizeUploadedHsCodeImportFileName(string fileName)
        {
            var normalized = Path.GetFileName(fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "hs-codes.xlsx";
            }

            if (!IsAllowedHsCodeImportFileName(normalized))
            {
                throw new ArgumentException("HS编码导入仅支持 .xlsx 或 .xlsm 文件。");
            }

            return normalized;
        }

        private static bool IsAllowedHsCodeImportFileName(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
        }
    }
}

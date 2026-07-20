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
                    var results = await hsCodeService.SearchRemoteAsync(keyword.Trim(), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var items = ApiMasterDataDtoFactory.FromHsCodes(results);
                    return Results.Ok(new ApiHsCodeSearchResponse(
                        items,
                        items.Count,
                        "remote",
                        "远程HS编码查询只读取在线来源并返回内存结果；保存到本地库时才写当前运行数据根数据库。"));
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
                    var response = await ResolveRemoteHsCodeDetailAsync(hsCodeService, request, cancellationToken);
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
        }

        private const string RemoteHsCodeDetailResolutionStoragePolicy =
            "HS编码联网详情补全只访问在线来源；有效编码写入当前运行数据根数据库用于下次本地查询，过期编码只从本次结果中清理，不新增默认目录或系统 C 盘落点。";

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
            ApiHsCodeDto request,
            CancellationToken cancellationToken)
        {
            var workingItems = new List<HsCode> { ApiMasterDataDtoFactory.ToHsCodeForSave(request) };
            var removedItems = new List<HsCode>();
            var updatedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void UpsertItem(HsCode item)
            {
                var normalizedCode = HsCodeTextHelper.NormalizeCode(item?.Code);
                if (item == null || string.IsNullOrWhiteSpace(normalizedCode) || HsCodeTextHelper.IsExpired(item))
                {
                    return;
                }

                item.Code = normalizedCode;
                var existing = workingItems.FirstOrDefault(current =>
                    string.Equals(HsCodeTextHelper.NormalizeCode(current?.Code), normalizedCode, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    workingItems.Add(item);
                }
                else
                {
                    CopyHsCodeFields(existing, item);
                }

                updatedCodes.Add(normalizedCode);
            }

            void RemoveItem(HsCode item)
            {
                var normalizedCode = HsCodeTextHelper.NormalizeCode(item?.Code);
                if (item == null || string.IsNullOrWhiteSpace(normalizedCode))
                {
                    return;
                }

                workingItems.RemoveAll(current =>
                    string.Equals(HsCodeTextHelper.NormalizeCode(current?.Code), normalizedCode, StringComparison.OrdinalIgnoreCase));

                if (removedCodes.Add(normalizedCode))
                {
                    item.Code = normalizedCode;
                    removedItems.Add(item);
                }
            }

            void AddItems(List<HsCode> items)
            {
                foreach (var item in items ?? Enumerable.Empty<HsCode>())
                {
                    UpsertItem(item);
                }
            }

            await hsCodeService.ProcessRemainingDetailsAsync(
                workingItems.ToList(),
                UpsertItem,
                RemoveItem,
                AddItems,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var visibleItems = workingItems
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !HsCodeTextHelper.IsExpired(item))
                .DistinctBy(item => HsCodeTextHelper.NormalizeCode(item.Code), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var message = BuildRemoteHsCodeDetailResolutionMessage(updatedCodes.Count, removedItems.Count);

            return new ApiHsCodeRemoteDetailResolutionResponse(
                ApiMasterDataDtoFactory.FromHsCodes(visibleItems),
                ApiMasterDataDtoFactory.FromHsCodes(removedItems),
                updatedCodes.Count,
                removedItems.Count,
                message,
                RemoteHsCodeDetailResolutionStoragePolicy);
        }

        private static string BuildRemoteHsCodeDetailResolutionMessage(int updatedCount, int removedCount)
        {
            if (removedCount > 0 && updatedCount > 0)
            {
                return $"已清理 {removedCount} 条过期HS编码，并补入或更新 {updatedCount} 条可用编码。";
            }

            if (removedCount > 0)
            {
                return $"已清理 {removedCount} 条过期HS编码，暂未找到可替换编码。";
            }

            if (updatedCount > 0)
            {
                return $"已补全 {updatedCount} 条HS编码详情。";
            }

            return "远程详情暂未补全，可稍后重试。";
        }

        private static void CopyHsCodeFields(HsCode target, HsCode source)
        {
            target.Code = HsCodeTextHelper.NormalizeCode(source.Code);
            target.Name = source.Name;
            target.Unit = source.Unit;
            target.Description = source.Description;
            target.Elements = source.Elements;
            target.SupervisionConditions = source.SupervisionConditions;
            target.InspectionCategory = source.InspectionCategory;
            target.RebateRate = source.RebateRate;
            target.NormalTariffRate = source.NormalTariffRate;
            target.PreferentialTariffRate = source.PreferentialTariffRate;
            target.ExportTariffRate = source.ExportTariffRate;
            target.ConsumptionTaxRate = source.ConsumptionTaxRate;
            target.ValueAddedTaxRate = source.ValueAddedTaxRate;
            target.Notes = source.Notes;
            target.UpdateTime = source.UpdateTime;
            target.DetailUrl = source.DetailUrl;
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

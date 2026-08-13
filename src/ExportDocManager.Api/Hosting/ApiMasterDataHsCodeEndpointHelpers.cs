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
                request.ObservedAt ?? DateTimeOffset.UtcNow);
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
                    PageSize = 1
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

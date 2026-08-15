using System.Text.Json;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static readonly JsonSerializerOptions ReferenceCatalogJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static void MapSingleWindowReferenceCatalogEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/single-window/reference-catalog", async (
                HttpContext context,
                ISingleWindowReferenceCatalogService referenceCatalogService,
                CancellationToken cancellationToken) =>
            {

                var catalog = await referenceCatalogService.LoadEffectiveCatalogAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromReferenceCatalog(catalog));
            })
            .WithName("GetSingleWindowReferenceCatalog")
            .Produces<ApiSingleWindowReferenceCatalogResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            endpoints.MapPut("/api/single-window/reference-catalog", async (
                HttpContext context,
                ISingleWindowReferenceCatalogService referenceCatalogService,
                ApiSingleWindowReferenceCatalogSaveRequest request,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (request?.Catalog == null)
                {
                    return Results.BadRequest(new ApiErrorResponse("单一窗口参考词典请求体不能为空。"));
                }

                var validationErrors = ValidateReferenceCatalog(request.Catalog);
                if (validationErrors.Count > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "单一窗口参考词典校验失败：" + string.Join("；", validationErrors.Take(8))));
                }

                await referenceCatalogService.SaveOverrideCatalogAsync(request.Catalog, cancellationToken);
                var catalog = await referenceCatalogService.LoadEffectiveCatalogAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromSavedReferenceCatalog(
                    catalog,
                    "单一窗口参考词典覆盖文件已保存。"));
            })
            .WithName("UpdateSingleWindowReferenceCatalog")
            .Produces<ApiSingleWindowReferenceCatalogSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/reference-catalog/import-json", async (
                HttpContext context,
                ISingleWindowReferenceCatalogService referenceCatalogService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                try
                {
                    var catalog = await JsonSerializer.DeserializeAsync<SingleWindowReferenceCatalogModel>(
                        context.Request.Body,
                        ReferenceCatalogJsonOptions,
                        cancellationToken);
                    if (catalog == null)
                    {
                        return Results.BadRequest(new ApiErrorResponse("单一窗口参考词典 JSON 内容不能为空。"));
                    }

                    var validationErrors = ValidateReferenceCatalog(catalog);
                    if (validationErrors.Count > 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse(
                            "单一窗口参考词典校验失败：" + string.Join("；", validationErrors.Take(8))));
                    }

                    await referenceCatalogService.SaveOverrideCatalogAsync(catalog, cancellationToken);
                    var effectiveCatalog = await referenceCatalogService.LoadEffectiveCatalogAsync(cancellationToken);
                    return Results.Ok(ApiSingleWindowDtoFactory.FromSavedReferenceCatalog(
                        effectiveCatalog,
                        "单一窗口参考词典 JSON 已导入并保存为覆盖文件。"));
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(new ApiErrorResponse($"单一窗口参考词典 JSON 格式无效：{ex.Message}"));
                }
            })
            .Accepts<byte[]>("application/json")
            .WithName("ImportSingleWindowReferenceCatalogJson")
            .Produces<ApiSingleWindowReferenceCatalogSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapPost("/api/single-window/reference-catalog/excel/preview", async (
                HttpContext context,
                ISingleWindowReferenceCatalogExcelImportService excelImportService,
                string? fileName,
                string? catalogKey,
                string? sheetName,
                int? headerRowNumber,
                int? dataStartRowNumber,
                int? codeColumn,
                int? englishNameColumn,
                int? chineseNameColumn,
                int? acdCodeColumn,
                int? alphaCodeColumn,
                int? nameColumn,
                int? descriptionColumn,
                int? valueColumn,
                int? aliasesColumn,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                fileName ??= string.Empty;
                if (!IsSupportedReferenceCatalogExcelFileName(fileName))
                {
                    return Results.BadRequest(new ApiErrorResponse("参考词典 Excel 导入只支持 .xlsx 或 .xlsm 文件。"));
                }

                var options = new SingleWindowReferenceCatalogExcelImportOptions(
                    catalogKey ?? string.Empty,
                    sheetName ?? string.Empty,
                    headerRowNumber is > 0 ? headerRowNumber.Value : 0,
                    dataStartRowNumber is > 0 ? dataStartRowNumber.Value : 2,
                    BuildReferenceCatalogExcelColumnMap(
                        codeColumn,
                        englishNameColumn,
                        chineseNameColumn,
                        acdCodeColumn,
                        alphaCodeColumn,
                        nameColumn,
                        descriptionColumn,
                        valueColumn,
                        aliasesColumn));

                try
                {
                    using var workbook = new MemoryStream();
                    await ApiUploadLimits.CopyRequestBodyAsync(
                        context.Request,
                        workbook,
                        ApiUploadLimits.ExcelImportBytes,
                        cancellationToken);
                    if (workbook.Length == 0)
                    {
                        return Results.BadRequest(new ApiErrorResponse("参考词典 Excel 文件不能为空。"));
                    }

                    workbook.Position = 0;
                    var preview = await excelImportService.PreviewImportAsync(
                        workbook,
                        options,
                        cancellationToken);
                    return Results.Ok(ApiSingleWindowDtoFactory.FromReferenceCatalogExcelPreview(preview));
                }
                catch (PayloadLimitExceededException ex)
                {
                    return WritePayloadTooLarge(ex);
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
                    return WriteServiceException(ex);
                }
            })
            .Accepts<IFormFile>("application/octet-stream")
            .WithName("PreviewSingleWindowReferenceCatalogExcelImport")
            .Produces<ApiSingleWindowReferenceCatalogExcelImportPreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

            endpoints.MapDelete("/api/single-window/reference-catalog", async (
                HttpContext context,
                ISingleWindowReferenceCatalogService referenceCatalogService,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                await referenceCatalogService.ResetToBundledCatalogAsync(cancellationToken);
                var catalog = await referenceCatalogService.LoadEffectiveCatalogAsync(cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromSavedReferenceCatalog(
                    catalog,
                    "单一窗口参考词典覆盖文件已清除，已恢复内置词典。"));
            })
            .WithName("ResetSingleWindowReferenceCatalog")
            .Produces<ApiSingleWindowReferenceCatalogSaveResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);
        }

        private static IReadOnlyList<string> ValidateReferenceCatalog(SingleWindowReferenceCatalogModel catalog)
        {
            var errors = new List<string>();

            ValidateDuplicateKeys(catalog.Countries?.Select(item => item?.Code), "COO国家/地区代码", errors);
            ValidateDuplicateKeys(catalog.AcdCountries?.Select(item => item?.Code), "ACD国别地区代码", errors);
            ValidateDuplicateKeys(catalog.Currencies?.Select(item => item?.Code), "币制标准数字代码", errors);
            ValidateDuplicateKeys(catalog.Currencies?.Select(item => item?.AcdCode), "ACD海关币制码", errors);
            ValidateDuplicateKeys(catalog.AcdTradeModes?.Select(item => item?.Code), "ACD贸易方式代码", errors);
            ValidateDuplicateKeys(catalog.TransportModes?.Select(item => item?.Value), "运输方式标准值", errors);
            ValidateDuplicateKeys(catalog.Ports?.Select(item => item?.Value), "港口标准值", errors);

            foreach (var (item, index) in (catalog.Countries ?? []).Select((item, index) => (item, index + 1)))
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Code) ||
                    string.IsNullOrWhiteSpace(item.EnglishName) ||
                    string.IsNullOrWhiteSpace(item.ChineseName))
                {
                    errors.Add($"COO国家/地区第 {index} 行必须填写代码、英文名和中文名");
                }
            }

            foreach (var (item, index) in (catalog.AcdCountries ?? []).Select((item, index) => (item, index + 1)))
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Code) ||
                    string.IsNullOrWhiteSpace(item.ChineseName) ||
                    string.IsNullOrWhiteSpace(item.EnglishName))
                {
                    errors.Add($"ACD国别地区第 {index} 行必须填写代码、中文简称和英文名");
                }
            }

            foreach (var (item, index) in (catalog.Currencies ?? []).Select((item, index) => (item, index + 1)))
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Code) ||
                    string.IsNullOrWhiteSpace(item.AlphaCode))
                {
                    errors.Add($"币制第 {index} 行必须填写标准数字代码和字母代码");
                }
            }

            foreach (var (item, index) in (catalog.AcdTradeModes ?? []).Select((item, index) => (item, index + 1)))
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.Code) ||
                    string.IsNullOrWhiteSpace(item.Name))
                {
                    errors.Add($"ACD贸易方式第 {index} 行必须填写代码和简称");
                }
            }

            foreach (var (item, index) in (catalog.TransportModes ?? []).Select((item, index) => (item, index + 1)))
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Value))
                {
                    errors.Add($"运输方式第 {index} 行必须填写标准值");
                }
            }

            foreach (var (item, index) in (catalog.Ports ?? []).Select((item, index) => (item, index + 1)))
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Value))
                {
                    errors.Add($"港口第 {index} 行必须填写标准值");
                }
            }

            return errors;
        }

        private static bool IsSupportedReferenceCatalogExcelFileName(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return string.IsNullOrWhiteSpace(extension) ||
                string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, int> BuildReferenceCatalogExcelColumnMap(
            int? codeColumn,
            int? englishNameColumn,
            int? chineseNameColumn,
            int? acdCodeColumn,
            int? alphaCodeColumn,
            int? nameColumn,
            int? descriptionColumn,
            int? valueColumn,
            int? aliasesColumn)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            AddColumn("code", codeColumn);
            AddColumn("englishName", englishNameColumn);
            AddColumn("chineseName", chineseNameColumn);
            AddColumn("acdCode", acdCodeColumn);
            AddColumn("alphaCode", alphaCodeColumn);
            AddColumn("name", nameColumn);
            AddColumn("description", descriptionColumn);
            AddColumn("value", valueColumn);
            AddColumn("aliases", aliasesColumn);
            return map;

            void AddColumn(string fieldKey, int? columnNumber)
            {
                if (columnNumber is > 0)
                {
                    map[fieldKey] = columnNumber.Value;
                }
            }
        }

        private static void ValidateDuplicateKeys(IEnumerable<string?>? values, string label, ICollection<string> errors)
        {
            var duplicates = (values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value?.Trim() ?? string.Empty)
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (string duplicate in duplicates)
            {
                errors.Add($"{label}存在重复值：{duplicate}");
            }
        }
    }
}

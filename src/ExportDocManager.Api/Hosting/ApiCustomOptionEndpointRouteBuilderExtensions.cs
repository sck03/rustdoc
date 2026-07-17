using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string CustomOptionStoragePolicy =
            "自定义候选项只写运行数据根数据库 CustomOptions 表，用于 Tauri/Web 表单下拉候选；不写 appsettings.json、默认导出目录、系统用户数据目录、全局数据目录或系统盘默认落点，也不读取发票/报关与付款/报销对方数据域。";

        private static readonly IReadOnlyDictionary<string, CustomOptionDefinition> CustomOptionDefinitions =
            new Dictionary<string, CustomOptionDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["Currency"] = new("Currency", AppConstants.Currencies, true),
                ["PaymentTerms"] = new("PaymentTerms", AppConstants.PaymentTerms, true),
                ["PortOfLoading"] = new("PortOfLoading", [], true),
                ["PortOfDestination"] = new("PortOfDestination", [], true),
                ["TransportMode"] = new("TransportMode", AppConstants.TransportModes, true),
                ["SupervisionMode"] = new("SupervisionMode", AppConstants.SupervisionModes, true),
                ["PaymentMethod"] = new("PaymentMethod", AppConstants.PaymentMethods, true),
                ["PayeeCategory"] = new("PayeeCategory", [], true),
                ["Type"] = new("Type", AppConstants.TradeTypes, false)
            };

        private static void MapCustomOptionEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/custom-options/{optionType}", (
                HttpContext context,
                string optionType,
                IApiSessionTokenService tokenService,
                ICustomOptionService customOptionService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!TryGetCustomOptionDefinition(optionType, out var definition, out var error))
                {
                    return Results.BadRequest(new ApiErrorResponse(error));
                }

                return Results.Ok(BuildCustomOptionResponse(definition, customOptionService));
            })
            .WithName("ListCustomOptions");

            endpoints.MapPost("/api/custom-options/{optionType}", (
                HttpContext context,
                string optionType,
                ApiCustomOptionSaveRequest request,
                IApiSessionTokenService tokenService,
                ICustomOptionService customOptionService) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                if (!TryGetCustomOptionDefinition(optionType, out var definition, out var error))
                {
                    return Results.BadRequest(new ApiErrorResponse(error));
                }

                if (!definition.AllowCustomValues)
                {
                    return Results.BadRequest(new ApiErrorResponse($"{definition.OptionType} 使用固定内置候选值，不允许保存自定义选项。"));
                }

                string value = TextSearchHelper.NormalizeValue(request?.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return Results.BadRequest(new ApiErrorResponse("自定义选项不能为空。"));
                }

                customOptionService.SaveOption(definition.OptionType, value);
                return Results.Ok(BuildCustomOptionResponse(definition, customOptionService));
            })
            .WithName("SaveCustomOption");
        }

        private static bool TryGetCustomOptionDefinition(
            string optionType,
            out CustomOptionDefinition definition,
            out string error)
        {
            string normalizedType = TextSearchHelper.NormalizeValue(optionType);
            if (string.IsNullOrWhiteSpace(normalizedType))
            {
                definition = null;
                error = "自定义选项类型不能为空。";
                return false;
            }

            if (CustomOptionDefinitions.TryGetValue(normalizedType, out definition))
            {
                error = string.Empty;
                return true;
            }

            definition = null;
            error = $"不支持的自定义选项类型：{normalizedType}。";
            return false;
        }

        private static ApiCustomOptionListResponse BuildCustomOptionResponse(
            CustomOptionDefinition definition,
            ICustomOptionService customOptionService)
        {
            var predefinedOptions = NormalizeOptionValues(definition.PredefinedOptions);
            var customOptions = definition.AllowCustomValues
                ? NormalizeOptionValues(customOptionService.GetOptions(definition.OptionType))
                : [];
            var options = predefinedOptions
                .Concat(customOptions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ApiCustomOptionListResponse
            {
                OptionType = definition.OptionType,
                PredefinedOptions = predefinedOptions,
                CustomOptions = customOptions,
                Options = options,
                AllowCustomValues = definition.AllowCustomValues,
                StoragePolicy = CustomOptionStoragePolicy
            };
        }

        private static List<string> NormalizeOptionValues(IEnumerable<string> values)
        {
            return (values ?? [])
                .Select(TextSearchHelper.NormalizeValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed record CustomOptionDefinition(
            string OptionType,
            IReadOnlyList<string> PredefinedOptions,
            bool AllowCustomValues);
    }
}

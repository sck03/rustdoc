using ExportDocManager.Services.SingleWindow;
using ExportDocManager.ViewModels;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public const string IssuingAuthorityStoragePolicy =
            "COO 签证机构候选读取程序根 Resources/SingleWindow 内置 JSON 与运行数据根 SingleWindow 覆盖 JSON；API 只返回只读候选，不写数据库或默认导出目录。";

        public const string CustomsCooEditorOptionsStoragePolicy =
            "COO 编辑候选来自 Application 内置 catalog；API 只返回证型、申报、贸易、包装、原产标准等只读候选，不写数据库、配置文件、缓存目录或默认导出目录。";

        public static ApiSingleWindowIssuingAuthorityCatalogResponse FromIssuingAuthorityCatalog()
        {
            var options = CustomsCooIssuingAuthorityCatalog.GetOptions()
                .Select(option => new ApiSingleWindowIssuingAuthorityOptionDto(
                    option.Value ?? string.Empty,
                    string.IsNullOrWhiteSpace(option.Text) ? option.Value ?? string.Empty : option.Text,
                    CustomsCooIssuingAuthorityCatalog.ResolveApplicationAddress(option.Value ?? string.Empty)))
                .ToArray();

            return new ApiSingleWindowIssuingAuthorityCatalogResponse(
                options,
                IssuingAuthorityStoragePolicy);
        }

        public static ApiCustomsCooEditorOptionsResponse FromCustomsCooEditorOptions()
        {
            return new ApiCustomsCooEditorOptionsResponse(
                ToApiOptions(CustomsCooEditorCatalog.ApplyTypeOptions),
                ToApiOptions(CustomsCooEditorCatalog.CertStatusOptions),
                ToApiOptions(CustomsCooEditorCatalog.CertTypeOptions),
                ToApiOptions(CustomsCooEditorCatalog.ProducerSecretOptions),
                ToApiOptions(CustomsCooEditorCatalog.ExhibitFlagOptions),
                ToApiOptions(CustomsCooEditorCatalog.ThirdPartyInvoiceOptions),
                ToApiOptions(CustomsCooEditorCatalog.PredictFlagOptions),
                ToApiOptions(CustomsCooEditorCatalog.PromiseOptions),
                ToApiOptions(CustomsCooEditorCatalog.CurrencyOptions),
                ToApiOptions(CustomsCooEditorCatalog.CooTradeModeOptions),
                ToApiOptions(CustomsCooEditorCatalog.GoodsItemFlagOptions),
                ToApiOptions(CustomsCooEditorCatalog.PackTypeOptions),
                ToApiOptions(CustomsCooEditorCatalog.GoodsTaxRateOptions),
                ToApiOptions(CustomsCooPackUnitCatalog.CommonOptions),
                BuildOriginCriteriaOptionSets(),
                BuildOriginCriteriaSubOptionSets(),
                CustomsCooEditorOptionsStoragePolicy);
        }

        private static ApiCustomsCooOptionDto[] ToApiOptions(IEnumerable<SelectionOption<string>> options)
        {
            return (options ?? Enumerable.Empty<SelectionOption<string>>())
                .Select(option => new ApiCustomsCooOptionDto(
                    option.Value ?? string.Empty,
                    string.IsNullOrWhiteSpace(option.Text) ? option.Value ?? string.Empty : option.Text))
                .ToArray();
        }

        private static ApiCustomsCooOriginCriteriaOptionSetDto[] BuildOriginCriteriaOptionSets()
        {
            return GetKnownCustomsCooCertificateTypes()
                .Select(certType => new ApiCustomsCooOriginCriteriaOptionSetDto(
                    certType,
                    string.Empty,
                    ToApiOptions(CustomsCooOriginCriteriaCatalog.GetOriginCriteriaOptions(certType))))
                .ToArray();
        }

        private static ApiCustomsCooOriginCriteriaOptionSetDto[] BuildOriginCriteriaSubOptionSets()
        {
            var sets = new List<ApiCustomsCooOriginCriteriaOptionSetDto>();
            foreach (string certType in GetKnownCustomsCooCertificateTypes())
            {
                foreach (var originCriteria in CustomsCooOriginCriteriaCatalog.GetOriginCriteriaOptions(certType))
                {
                    string value = originCriteria.Value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var options = ToApiOptions(CustomsCooOriginCriteriaCatalog.GetOriginCriteriaSubOptions(certType, value));
                    if (options.All(option => string.IsNullOrWhiteSpace(option.Value)))
                    {
                        continue;
                    }

                    sets.Add(new ApiCustomsCooOriginCriteriaOptionSetDto(certType, value, options));
                }
            }

            return sets.ToArray();
        }

        private static IReadOnlyList<string> GetKnownCustomsCooCertificateTypes()
        {
            return CustomsCooEditorCatalog.CertTypeOptions
                .Select(option => option.Value?.Trim().ToUpperInvariant() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}

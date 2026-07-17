using System.Linq;

namespace ExportDocManager.Services.SingleWindow
{
    public enum CustomsCooGoodsFieldEditState
    {
        Editable = 0,
        ReadOnly = 1,
        Disabled = 2
    }

    public readonly record struct CustomsCooEditorRuleContext(
        string CertType,
        string CertStatus,
        string ApplyType,
        string ThirdPartyInvFlag,
        string CurrentOriginCriteria,
        string CurrentGoodsItemFlag,
        string CurrentPackType)
    {
        public string RuleKey => $"{ApplyType}|{CertStatus}|{CertType}|{ThirdPartyInvFlag}|{CurrentOriginCriteria}|{CurrentGoodsItemFlag}|{CurrentPackType}";
        public bool UsesTaiwanHeaderContacts => CustomsCooRuleCatalog.UsesTaiwanHeaderContacts(CertType);
        public bool RequiresTaiwanExporterContacts => CustomsCooRuleCatalog.RequiresTaiwanExporterContacts(CertType, ApplyType);
        public bool UsesNonpartyCorps => CustomsCooRuleCatalog.UsesThirdPartyInvoiceFlag(CertType) &&
                                          string.Equals(ThirdPartyInvFlag, "1", StringComparison.Ordinal);
        public bool IsNonGoodsItem => CustomsCooGoodsItemFlagCatalog.IsNonGoods(CurrentGoodsItemFlag);
        public bool IsIrregularPackItem => CustomsCooPackTypeCatalog.IsIrregular(CurrentPackType);
        public bool UsesModificationFields => CustomsCooRuleCatalog.UsesModificationFields(CertStatus);
        public bool UsesOriginCountryFields => CustomsCooRuleCatalog.UsesOriginCountryFields(CertType);
        public bool RequiresUiTransportDetails => CustomsCooRuleCatalog.RequiresUiTransportDetails(CertType);
        public bool RequiresGspTransportAndTrade => CustomsCooRuleCatalog.RequiresGspTransportAndTrade(CertType);
        public bool RequiresRcepInvoiceInfo => CustomsCooRuleCatalog.UsesRcepInvoiceInfo(CertType);
        public bool UsesGoodsProducerDescription => CustomsCooRuleCatalog.UsesGoodsProducerDescription(CertType);
        public bool UsesGoodsProducerContactFields => CustomsCooRuleCatalog.UsesGoodsProducerContactFields(CertType);
        public bool UsesGoodsRcepFields => CustomsCooRuleCatalog.UsesGoodsRcepFields(CertType);
        public bool UsesGoodsOriginCriteria => CustomsCooRuleCatalog.UsesGoodsOriginCriteria(CertType);
        public bool RequiresGoodsOriginCriteria => CustomsCooOriginCriteriaCatalog.RequiresOriginCriteria(CertType);
        public bool UsesGoodsOriCriteriaSub => CustomsCooRuleCatalog.UsesGoodsOriCriteriaSub(CertType);
        public bool RequiresOriginCriteriaRef => CustomsCooOriginCriteriaCatalog.RequiresOriginCriteriaRef(CertType, CurrentOriginCriteria);
        public bool UsesOriginCriteriaRef =>
            string.Equals(CertType, "E", StringComparison.Ordinal) ||
            RequiresOriginCriteriaRef;
        public bool ShowsGoodsConditionalExtension =>
            UsesGoodsRcepFields ||
            UsesGoodsProducerDescription ||
            UsesGoodsProducerContactFields ||
            UsesGoodsOriCriteriaSub ||
            UsesOriginCriteriaRef;
    }

    public static class CustomsCooEditorRuleCatalog
    {
        private sealed record EditorFieldRule(
            string PropertyName,
            Func<CustomsCooEditorRuleContext, bool> IsVisible,
            Func<CustomsCooEditorRuleContext, bool> IsRequired = null);

        private static readonly EditorFieldRule[] HeaderFieldRules =
        [
            new("Remark", context => CustomsCooRuleCatalog.UsesHeaderRemark(context.CertType)),
            new(
                "Producer",
                context => CustomsCooRuleCatalog.UsesHeaderProducer(context.CertType),
                context => CustomsCooRuleCatalog.RequiresHeaderProducer(context.CertType, context.ThirdPartyInvFlag)),
            new("ExhibitFlag", context => CustomsCooRuleCatalog.UsesExhibitFlag(context.CertType)),
            new("ThirdPartyInvFlag", context => CustomsCooRuleCatalog.UsesThirdPartyInvoiceFlag(context.CertType)),
            new("OriCountryCode", context => context.UsesOriginCountryFields),
            new("OriCountry", context => context.UsesOriginCountryFields),
            new("PrcsAssembly", context => CustomsCooRuleCatalog.UsesProcessingAssembly(context.CertType)),
            new("OldCertNo", context => context.UsesModificationFields, context => context.UsesModificationFields),
            new("ModReason", context => context.UsesModificationFields, context => context.UsesModificationFields),
            new("ModColm", context => context.UsesModificationFields),
            new("OldSituDesc", context => context.UsesModificationFields),
            new("ModSituDesc", context => context.UsesModificationFields),
            new("OldDeclDate", context => context.UsesModificationFields),
            new("OldIssueDate", context => context.UsesModificationFields),
            new("PredictFlag", context => CustomsCooRuleCatalog.UsesPredictFlag(context.CertType)),
            new("InvDate", _ => true, context => context.RequiresRcepInvoiceInfo),
            new("InvNo", _ => true, context => context.RequiresRcepInvoiceInfo),
            new("TransDetails", _ => true, context => context.RequiresUiTransportDetails),
            new("TradeModeCode", _ => true, context => context.RequiresGspTransportAndTrade),
            new("Curr", _ => true, context => context.RequiresRcepInvoiceInfo),
            new("ExporterTel", context => context.UsesTaiwanHeaderContacts, context => context.RequiresTaiwanExporterContacts),
            new("ExporterFax", context => context.UsesTaiwanHeaderContacts, context => context.RequiresTaiwanExporterContacts),
            new("ExporterEmail", context => context.UsesTaiwanHeaderContacts, context => context.RequiresTaiwanExporterContacts),
            new("ConsigneeTel", context => context.UsesTaiwanHeaderContacts),
            new("ConsigneeFax", context => context.UsesTaiwanHeaderContacts),
            new("ConsigneeEmail", context => context.UsesTaiwanHeaderContacts),
            new("EtpsConcEr", context => context.UsesTaiwanHeaderContacts),
            new("EtpsTel", context => context.UsesTaiwanHeaderContacts)
        ];

        private static readonly EditorFieldRule[] GoodsFieldRules =
        [
            new(
                "OriCriteria",
                context => context.UsesGoodsOriginCriteria,
                context => context.RequiresGoodsOriginCriteria),
            new(
                "GoodsOriginCountry",
                context => context.UsesGoodsRcepFields,
                context => context.UsesGoodsRcepFields),
            new(
                "GoodsOriginCountryEn",
                context => context.UsesGoodsRcepFields,
                context => context.UsesGoodsRcepFields),
            new(
                "InvNo",
                context => context.UsesGoodsRcepFields,
                context => context.UsesGoodsRcepFields),
            new(
                "ICompPrpr",
                context => context.UsesGoodsRcepFields,
                context => context.UsesGoodsRcepFields),
            new("GoodsTaxRate", context => context.UsesGoodsRcepFields),
            new("Producer", context => context.UsesGoodsProducerDescription),
            new("ProducerTel", context => context.UsesGoodsProducerContactFields),
            new("ProducerFax", context => context.UsesGoodsProducerContactFields),
            new("ProducerEmail", context => context.UsesGoodsProducerContactFields),
            new("ProducerSertFlag", context => context.UsesGoodsProducerContactFields),
            new("OriCriteriaSub", context => context.UsesGoodsOriCriteriaSub),
            new(
                "OriCriteriaRef",
                context => context.UsesOriginCriteriaRef,
                context => context.RequiresOriginCriteriaRef)
        ];

        private static readonly HashSet<string> NonGoodsReadOnlyFields = new(StringComparer.Ordinal)
        {
            "GoodsName"
        };

        private static readonly HashSet<string> NonGoodsDisabledFields = new(StringComparer.Ordinal)
        {
            "HSCode",
            "GoodsQty",
            "GoodsQtyRef",
            "GoodsUnitE",
            "GoodsUnit",
            "GoodsUnitRef",
            "SecdGoodsQtyRef",
            "SecdGoodsUnitRef",
            "InvPrice",
            "InvValue",
            "FobValue",
            "ICompPrpr",
            "OriCriteria",
            "OriCriteriaRef",
            "OriCriteriaSub",
            "GoodsOriginCountry",
            "GoodsOriginCountryEn",
            "GoodsTaxRate",
            "InvNo",
            "Producer",
            "ProducerTel",
            "ProducerFax",
            "ProducerEmail",
            "ProducerSertFlag",
            "CiqRegNo",
            "PrdcEtpsName",
            "PrdcEtpsConcEr",
            "PrdcEtpsTel"
        };

        public static CustomsCooEditorRuleContext CreateContext(
            string certType,
            string certStatus,
            string applyType,
            string thirdPartyInvFlag,
            string currentOriginCriteria,
            string currentGoodsItemFlag = "",
            string currentPackType = "")
        {
            return new CustomsCooEditorRuleContext(
                NormalizeUpper(certType),
                certStatus?.Trim() ?? string.Empty,
                applyType?.Trim() ?? string.Empty,
                thirdPartyInvFlag?.Trim() ?? string.Empty,
                NormalizeUpper(currentOriginCriteria),
                NormalizeUpper(currentGoodsItemFlag),
                currentPackType?.Trim() ?? string.Empty);
        }

        public static void ApplyHeaderFieldRules(CustomsCooEditorRuleContext context, Action<string, bool, bool> apply)
        {
            ApplyFieldRules(HeaderFieldRules, context, apply);
        }

        public static void ApplyGoodsFieldRules(CustomsCooEditorRuleContext context, Action<string, bool, bool> apply)
        {
            ApplyFieldRules(GoodsFieldRules, context, apply);
        }

        public static bool IsGoodsFieldEditable(CustomsCooEditorRuleContext context, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(propertyName);
            return GetGoodsFieldEditState(context, propertyName) == CustomsCooGoodsFieldEditState.Editable;
        }

        public static CustomsCooGoodsFieldEditState GetGoodsFieldEditState(CustomsCooEditorRuleContext context, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(propertyName);

            if (!context.IsNonGoodsItem)
            {
                return CustomsCooGoodsFieldEditState.Editable;
            }

            if (NonGoodsReadOnlyFields.Contains(propertyName))
            {
                return CustomsCooGoodsFieldEditState.ReadOnly;
            }

            return NonGoodsDisabledFields.Contains(propertyName)
                ? CustomsCooGoodsFieldEditState.Disabled
                : CustomsCooGoodsFieldEditState.Editable;
        }

        public static bool IsGoodsFieldConditionallyRequired(CustomsCooEditorRuleContext context, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(propertyName);

            var rule = GoodsFieldRules.FirstOrDefault(item =>
                string.Equals(item.PropertyName, propertyName, StringComparison.Ordinal));
            return rule?.IsRequired?.Invoke(context) ?? false;
        }

        private static void ApplyFieldRules(
            IEnumerable<EditorFieldRule> rules,
            CustomsCooEditorRuleContext context,
            Action<string, bool, bool> apply)
        {
            foreach (var rule in rules)
            {
                apply(rule.PropertyName, rule.IsVisible(context), rule.IsRequired?.Invoke(context) ?? false);
            }
        }

        private static string NormalizeUpper(string value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}


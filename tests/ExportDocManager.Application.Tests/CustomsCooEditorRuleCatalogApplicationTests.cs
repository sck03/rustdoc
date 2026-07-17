using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooEditorRuleCatalogApplicationTests
    {
        [Fact]
        public void ApplyGoodsFieldRules_ShouldRequireRcepOriginFields()
        {
            var context = CustomsCooEditorRuleCatalog.CreateContext(
                certType: "RC",
                certStatus: "0",
                applyType: "0",
                thirdPartyInvFlag: string.Empty,
                currentOriginCriteria: string.Empty);

            var rules = CaptureGoodsRules(context);

            Assert.Equal((true, true), rules["OriCriteria"]);
            Assert.Equal((true, true), rules["GoodsOriginCountry"]);
            Assert.Equal((true, true), rules["GoodsOriginCountryEn"]);
            Assert.Equal((true, true), rules["ICompPrpr"]);
            Assert.Equal((true, true), rules["InvNo"]);
            Assert.Equal((false, false), rules["OriCriteriaRef"]);
        }

        [Theory]
        [InlineData("C", "", false, false, false, false, false)]
        [InlineData("G", "P", true, false, false, false, false)]
        [InlineData("G", "W", true, true, false, false, false)]
        [InlineData("G", "Y", true, true, false, false, false)]
        [InlineData("A", "WO", true, false, false, false, false)]
        [InlineData("F", "WO", true, false, false, false, false)]
        [InlineData("RC", "RVC", true, false, true, true, true)]
        public void ApplyGoodsFieldRules_ShouldMatchCommonCertificateVisibility(
            string certType,
            string originCriteria,
            bool showOriginCriteria,
            bool showOriginCriteriaRef,
            bool showOriginCountry,
            bool showGoodsTaxRate,
            bool showProducer)
        {
            var context = CustomsCooEditorRuleCatalog.CreateContext(
                certType: certType,
                certStatus: "0",
                applyType: "0",
                thirdPartyInvFlag: string.Empty,
                currentOriginCriteria: originCriteria);

            var rules = CaptureGoodsRules(context);

            Assert.Equal(showOriginCriteria, rules["OriCriteria"].Visible);
            Assert.Equal(showOriginCriteriaRef, rules["OriCriteriaRef"].Visible);
            Assert.Equal(showOriginCriteriaRef, rules["OriCriteriaRef"].Required);
            Assert.Equal(showOriginCountry, rules["GoodsOriginCountry"].Visible);
            Assert.Equal(showOriginCountry, rules["ICompPrpr"].Visible);
            Assert.Equal(showOriginCountry, rules["InvNo"].Visible);
            Assert.Equal(showGoodsTaxRate, rules["GoodsTaxRate"].Visible);
            Assert.Equal(showProducer, rules["Producer"].Visible);
        }

        [Fact]
        public void GetGoodsFieldEditState_ShouldDisableNormalGoodsFieldsForNonGoodsItem()
        {
            var context = CustomsCooEditorRuleCatalog.CreateContext(
                certType: "RC",
                certStatus: "0",
                applyType: "0",
                thirdPartyInvFlag: string.Empty,
                currentOriginCriteria: string.Empty,
                currentGoodsItemFlag: "Y");

            Assert.Equal(CustomsCooGoodsFieldEditState.ReadOnly, CustomsCooEditorRuleCatalog.GetGoodsFieldEditState(context, "GoodsName"));
            Assert.Equal(CustomsCooGoodsFieldEditState.Disabled, CustomsCooEditorRuleCatalog.GetGoodsFieldEditState(context, "HSCode"));
            Assert.Equal(CustomsCooGoodsFieldEditState.Editable, CustomsCooEditorRuleCatalog.GetGoodsFieldEditState(context, "GoodsNameE"));
        }

        [Fact]
        public void ApplyHeaderFieldRules_ShouldRequireModificationFieldsForModifyCertificate()
        {
            var context = CustomsCooEditorRuleCatalog.CreateContext(
                certType: "C",
                certStatus: "1",
                applyType: "0",
                thirdPartyInvFlag: string.Empty,
                currentOriginCriteria: string.Empty);

            var rules = CaptureHeaderRules(context);

            Assert.Equal((true, true), rules["OldCertNo"]);
            Assert.Equal((true, true), rules["ModReason"]);
            Assert.Equal((true, false), rules["ModColm"]);
        }

        private static Dictionary<string, (bool Visible, bool Required)> CaptureHeaderRules(CustomsCooEditorRuleContext context)
        {
            var rules = new Dictionary<string, (bool Visible, bool Required)>(StringComparer.Ordinal);
            CustomsCooEditorRuleCatalog.ApplyHeaderFieldRules(context, (propertyName, visible, required) =>
            {
                rules[propertyName] = (visible, required);
            });

            return rules;
        }

        private static Dictionary<string, (bool Visible, bool Required)> CaptureGoodsRules(CustomsCooEditorRuleContext context)
        {
            var rules = new Dictionary<string, (bool Visible, bool Required)>(StringComparer.Ordinal);
            CustomsCooEditorRuleCatalog.ApplyGoodsFieldRules(context, (propertyName, visible, required) =>
            {
                rules[propertyName] = (visible, required);
            });

            return rules;
        }
    }
}

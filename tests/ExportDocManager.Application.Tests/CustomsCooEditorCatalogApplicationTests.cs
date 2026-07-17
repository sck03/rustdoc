using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooEditorCatalogApplicationTests
    {
        [Fact]
        public void ResolveHeaderSectionKey_ShouldMapKnownFields()
        {
            Assert.Equal(
                "运输与贸易",
                CustomsCooEditorCatalog.ResolveHeaderSectionKey("TradeModeCode"));
            Assert.Equal(
                "更改与重发",
                CustomsCooEditorCatalog.ResolveHeaderSectionKey("OldCertNo"));
            Assert.Equal(
                CustomsCooEditorCatalog.DefaultHeaderSectionKey,
                CustomsCooEditorCatalog.ResolveHeaderSectionKey("UnknownField"));
        }

        [Fact]
        public void GetHeaderSectionFields_ShouldExposeReusableFieldMetadata()
        {
            var declarationFields = CustomsCooEditorCatalog.GetHeaderSectionFields("申报与对象");
            var tradeFields = CustomsCooEditorCatalog.GetHeaderSectionFields("运输与贸易");

            var issuingAuthority = Assert.Single(
                declarationFields,
                field => string.Equals(field.PropertyName, "OrgCode", StringComparison.Ordinal));
            var tradeMode = Assert.Single(
                tradeFields,
                field => string.Equals(field.PropertyName, "TradeModeCode", StringComparison.Ordinal));

            Assert.Equal(CustomsCooEditorFieldKind.EditableComboBox, issuingAuthority.FieldKind);
            Assert.Equal(CustomsCooEditorFieldKind.ComboBox, tradeMode.FieldKind);
            Assert.NotNull(issuingAuthority.Options);
            Assert.NotEmpty(tradeMode.Options);
        }

        [Fact]
        public void CooOptions_ShouldUseOriginCertificateCodeSystem()
        {
            Assert.Contains(
                CustomsCooEditorCatalog.CooTradeModeOptions,
                option => option.Value == "1" && option.Text.Contains("一般贸易", StringComparison.Ordinal));
            Assert.DoesNotContain(
                CustomsCooEditorCatalog.CooTradeModeOptions,
                option => option.Value == "0110");

            Assert.Contains(
                CustomsCooEditorCatalog.CurrencyOptions,
                option => option.Value == "USD" && option.Text.Contains("美元", StringComparison.Ordinal));
            Assert.DoesNotContain(
                CustomsCooEditorCatalog.CurrencyOptions,
                option => option.Value == "502");
        }

        [Fact]
        public void GoodsDetailFields_ShouldExposeReusableGoodsMetadata()
        {
            var packUnit = Assert.Single(
                CustomsCooEditorCatalog.RequiredGoodsDetailFields,
                field => string.Equals(field.PropertyName, "PackUnit", StringComparison.Ordinal));
            var goodsDescription = Assert.Single(
                CustomsCooEditorCatalog.OriginAndEnterpriseGoodsDetailFields,
                field => string.Equals(field.PropertyName, "GoodsDesc", StringComparison.Ordinal));
            var producerSecret = Assert.Single(
                CustomsCooEditorCatalog.AgreementAndProducerGoodsDetailFields,
                field => string.Equals(field.PropertyName, "ProducerSertFlag", StringComparison.Ordinal));

            Assert.Equal(CustomsCooEditorFieldKind.EditableComboBox, packUnit.FieldKind);
            Assert.Equal(CustomsCooEditorFieldKind.MultilineTextBox, goodsDescription.FieldKind);
            Assert.Equal(64, goodsDescription.Height);
            Assert.Equal(CustomsCooEditorFieldKind.ComboBox, producerSecret.FieldKind);
        }

        [Fact]
        public void ScopedClearMetadata_ShouldExposeHeaderGoodsAndAttachmentCategories()
        {
            Assert.Contains("证书基础", CustomsCooEditorCatalog.ScopedClearOptionsByGroup.Keys);
            Assert.Contains("明细项目", CustomsCooEditorCatalog.ScopedClearOptionsByGroup.Keys);
            Assert.Contains("附件", CustomsCooEditorCatalog.ScopedClearOptionsByGroup.Keys);

            Assert.Contains("GoodsDesc", CustomsCooEditorCatalog.ScopedGoodsFieldKeysByCategory["goods_name_pack"]);
            Assert.Contains("ProducerSertFlag", CustomsCooEditorCatalog.ScopedGoodsFieldKeysByCategory["goods_producer"]);
            Assert.Contains("Description", CustomsCooEditorCatalog.ScopedAttachmentStringFieldKeysByCategory["attachment_note_delay"]);
        }

        [Fact]
        public void RequiredAndDefaultMetadata_ShouldContainOfficialCooFields()
        {
            Assert.Equal("0", CustomsCooEditorCatalog.HeaderDefaultFallbacks["ApplyType"]);
            Assert.Equal("C", CustomsCooEditorCatalog.HeaderDefaultFallbacks["CertType"]);
            Assert.Contains("AplRegNo", CustomsCooEditorCatalog.RequiredHeaderProperties);
            Assert.Contains("GoodsItemFlag", CustomsCooEditorCatalog.RequiredGoodsFieldProperties);
            Assert.Contains("GoodsDesc", CustomsCooEditorCatalog.RequiredGoodsFieldProperties);
            Assert.Contains("DraftRevision", CustomsCooEditorCatalog.HeaderRefreshProperties);
        }
    }
}

using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class AgentConsignmentEditorCatalogApplicationTests
    {
        [Fact]
        public void ResolveSectionKey_ShouldMapDeclarationFields()
        {
            Assert.Equal(
                AgentConsignmentEditorCatalog.DeclarationSectionKey,
                AgentConsignmentEditorCatalog.ResolveSectionKey("TradeMode"));
            Assert.Equal(
                AgentConsignmentEditorCatalog.ReceiptSectionKey,
                AgentConsignmentEditorCatalog.ResolveSectionKey("UnknownField"));
        }

        [Fact]
        public void GetSectionFields_ShouldExposeDeclarationReferenceFields()
        {
            var fields = AgentConsignmentEditorCatalog.GetSectionFields(
                AgentConsignmentEditorCatalog.DeclarationSectionKey);

            var tradeMode = Assert.Single(fields, field => string.Equals(field.PropertyName, "TradeMode", StringComparison.Ordinal));
            var originCountry = Assert.Single(fields, field => string.Equals(field.PropertyName, "OriCountry", StringComparison.Ordinal));

            Assert.Equal(AgentConsignmentEditorFieldKind.EditableComboBox, tradeMode.FieldKind);
            Assert.Equal(AgentConsignmentEditorFieldKind.EditableComboBox, originCountry.FieldKind);
            Assert.NotEmpty(tradeMode.Options);
            Assert.NotEmpty(originCountry.Options);
        }

        [Fact]
        public void DeclarationReferenceFields_ShouldUseAcdCodeSystem()
        {
            var fields = AgentConsignmentEditorCatalog.GetSectionFields(
                AgentConsignmentEditorCatalog.DeclarationSectionKey);

            var tradeMode = Assert.Single(fields, field => string.Equals(field.PropertyName, "TradeMode", StringComparison.Ordinal));
            var originCountry = Assert.Single(fields, field => string.Equals(field.PropertyName, "OriCountry", StringComparison.Ordinal));

            Assert.Contains(
                tradeMode.Options,
                option => option.Value == "0110" && option.Text.Contains("一般贸易", StringComparison.Ordinal));
            Assert.DoesNotContain(
                tradeMode.Options,
                option => option.Value == "1" && option.Text.Contains("一般贸易", StringComparison.Ordinal));
            Assert.Contains(
                originCountry.Options,
                option => option.Value == "142" && option.Text.Contains("中国", StringComparison.Ordinal));
        }

        [Fact]
        public void RequiredFieldProperties_ShouldContainOfficialImportFields()
        {
            Assert.Contains("CopCusCode", AgentConsignmentEditorCatalog.RequiredFieldProperties);
            Assert.Contains("TradeMode", AgentConsignmentEditorCatalog.RequiredFieldProperties);
            Assert.Contains("OriCountry", AgentConsignmentEditorCatalog.RequiredFieldProperties);
            Assert.Contains("AgentCode", AgentConsignmentEditorCatalog.RequiredFieldProperties);
        }

        [Fact]
        public void CueText_ShouldDescribeListNoManualInput()
        {
            string cueText = AgentConsignmentEditorCatalog.GetCueText("ListNo");

            Assert.Contains("真实提单号", cueText, StringComparison.Ordinal);
            Assert.Contains("不会再自动带发票号", cueText, StringComparison.Ordinal);
        }
    }
}

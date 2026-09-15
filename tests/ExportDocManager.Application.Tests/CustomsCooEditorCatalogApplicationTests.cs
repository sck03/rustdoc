using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooEditorCatalogApplicationTests
    {
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

    }
}

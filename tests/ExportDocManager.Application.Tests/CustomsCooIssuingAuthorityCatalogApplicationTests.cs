using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooIssuingAuthorityCatalogApplicationTests
    {
        [Fact]
        public void Catalog_ShouldResolveConfiguredEntriesWithoutFileSystem()
        {
            try
            {
                CustomsCooIssuingAuthorityCatalog.ConfigureEntrySnapshotLoader(
                    () =>
                    [
                        new CustomsCooIssuingAuthorityEntry(
                            " 9999 ",
                            "测试海关",
                            "TEST CITY, CHINA",
                            "测关",
                            "测试关")
                    ]);

                Assert.Equal("9999：测试海关", CustomsCooIssuingAuthorityCatalog.GetDisplayText("测关"));
                Assert.Equal("9999", CustomsCooIssuingAuthorityCatalog.ParseCode("测试关"));
                Assert.Equal("TEST CITY, CHINA", CustomsCooIssuingAuthorityCatalog.ResolveApplicationAddress(" 9999 "));
            }
            finally
            {
                CustomsCooIssuingAuthorityCatalog.ConfigureEntrySnapshotLoader(() => []);
            }
        }
    }
}

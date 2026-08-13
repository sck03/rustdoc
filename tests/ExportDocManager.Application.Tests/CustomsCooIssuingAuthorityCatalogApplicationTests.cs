using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooIssuingAuthorityCatalogApplicationTests
    {
        [Fact]
        public void Catalog_ShouldResolveConfiguredEntriesWithoutFileSystem()
        {
            var catalog = new CustomsCooIssuingAuthorityCatalog(
            [
                new CustomsCooIssuingAuthorityEntry(
                    " 9999 ",
                    "测试海关",
                    "TEST CITY, CHINA",
                    "测关",
                    "测试关")
            ]);

            Assert.Equal("9999：测试海关", catalog.GetDisplayText("测关"));
            Assert.Equal("9999", catalog.ParseCode("测试关"));
            Assert.Equal("TEST CITY, CHINA", catalog.ResolveApplicationAddress(" 9999 "));
        }
    }
}

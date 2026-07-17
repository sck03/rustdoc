using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowReferenceCatalogApplicationTests
    {
        [Fact]
        public void SingleWindowReferenceCatalogs_ShouldBuildFallbackAcdOptionsWithoutFileSystem()
        {
            try
            {
                SingleWindowReferenceCatalogs.ConfigureReferenceCatalogSnapshotLoader(
                    () => new SingleWindowReferenceCatalogModel());

                var tradeModeOptions = SingleWindowReferenceCatalogs.GetAcdTradeModeOptions();
                var countryOptions = SingleWindowReferenceCatalogs.GetAcdCountryOptions();

                Assert.Contains(tradeModeOptions, option => option.Value == "0110" && option.Text.Contains("一般贸易", StringComparison.Ordinal));
                Assert.Contains(countryOptions, option => option.Value == "142" && option.Text.Contains("中国", StringComparison.Ordinal));
            }
            finally
            {
                SingleWindowReferenceCatalogs.ConfigureReferenceCatalogSnapshotLoader(
                    () => new SingleWindowReferenceCatalogModel());
            }
        }

        [Fact]
        public void SingleWindowReferenceCatalogs_ShouldUseConfiguredSnapshot()
        {
            try
            {
                SingleWindowReferenceCatalogs.ConfigureReferenceCatalogSnapshotLoader(
                    () => new SingleWindowReferenceCatalogModel
                    {
                        AcdTradeModes =
                        [
                            new()
                            {
                                Code = "9999",
                                Name = "测试贸易",
                                Description = "测试说明"
                            }
                        ],
                        AcdCountries =
                        [
                            new()
                            {
                                Code = "998",
                                ChineseName = "测试国家",
                                EnglishName = "Test Country"
                            }
                        ]
                    });

                var tradeModeOption = Assert.Single(
                    SingleWindowReferenceCatalogs.GetAcdTradeModeOptions(),
                    option => option.Value == "9999");
                var countryOption = Assert.Single(
                    SingleWindowReferenceCatalogs.GetAcdCountryOptions(),
                    option => option.Value == "998");

                Assert.Equal("9999：测试贸易 - 测试说明", tradeModeOption.Text);
                Assert.Equal("998：测试国家 / Test Country", countryOption.Text);
            }
            finally
            {
                SingleWindowReferenceCatalogs.ConfigureReferenceCatalogSnapshotLoader(
                    () => new SingleWindowReferenceCatalogModel());
            }
        }
    }
}

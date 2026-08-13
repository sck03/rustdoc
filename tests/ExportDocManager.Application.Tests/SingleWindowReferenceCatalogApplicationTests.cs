using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowReferenceCatalogApplicationTests
    {
        [Fact]
        public void SingleWindowReferenceCatalogs_ShouldBuildFallbackAcdOptionsWithoutFileSystem()
        {
            var catalogs = new SingleWindowReferenceCatalogs(new SingleWindowReferenceCatalogModel());
            var tradeModeOptions = catalogs.GetAcdTradeModeOptions();
            var countryOptions = catalogs.GetAcdCountryOptions();

            Assert.Contains(tradeModeOptions, option => option.Value == "0110" && option.Text.Contains("一般贸易", StringComparison.Ordinal));
            Assert.Contains(countryOptions, option => option.Value == "142" && option.Text.Contains("中国", StringComparison.Ordinal));
        }

        [Fact]
        public void SingleWindowReferenceCatalogs_ShouldUseConfiguredSnapshot()
        {
            var catalogs = new SingleWindowReferenceCatalogs(
                new SingleWindowReferenceCatalogModel
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
                catalogs.GetAcdTradeModeOptions(),
                option => option.Value == "9999");
            var countryOption = Assert.Single(
                catalogs.GetAcdCountryOptions(),
                option => option.Value == "998");

            Assert.Equal("9999：测试贸易 - 测试说明", tradeModeOption.Text);
            Assert.Equal("998：测试国家 / Test Country", countryOption.Text);
        }

        [Fact]
        public void SingleWindowReferenceCatalogs_ShouldKeepInstancesIsolated()
        {
            var first = new SingleWindowReferenceCatalogs(new SingleWindowReferenceCatalogModel
            {
                AcdTradeModes = [new() { Code = "9001", Name = "实例一" }]
            });
            var second = new SingleWindowReferenceCatalogs(new SingleWindowReferenceCatalogModel
            {
                AcdTradeModes = [new() { Code = "9002", Name = "实例二" }]
            });

            Assert.Contains(first.GetAcdTradeModeOptions(), option => option.Value == "9001");
            Assert.DoesNotContain(first.GetAcdTradeModeOptions(), option => option.Value == "9002");
            Assert.Contains(second.GetAcdTradeModeOptions(), option => option.Value == "9002");
        }
    }
}

using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowFieldMapperHelperApplicationTests
    {
        [Fact]
        public void Helpers_ShouldUseConfiguredReferenceCatalogSnapshot()
        {
            var helpers = new SingleWindowFieldMapperHelpers(
                new SingleWindowReferenceCatalogModel
                {
                    Countries =
                        [
                            new()
                            {
                                Code = "998",
                                EnglishName = "TESTLAND",
                                ChineseName = "测试国家",
                                Aliases = ["测试国"]
                            }
                        ],
                    AcdCountries =
                        [
                            new()
                            {
                                Code = "997",
                                ChineseName = "测试货源地",
                                EnglishName = "Test Origin",
                                Aliases = ["测试货源"]
                            }
                        ],
                    Currencies =
                        [
                            new()
                            {
                                Code = "996",
                                AcdCode = "996",
                                AlphaCode = "TST",
                                Aliases = ["测试币"]
                            }
                        ],
                    AcdTradeModes =
                        [
                            new()
                            {
                                Code = "9999",
                                Name = "测试贸易",
                                Description = "测试说明",
                                Aliases = ["测试模式"]
                            }
                        ],
                    TransportModes =
                        [
                            new()
                            {
                                Value = "BY TEST",
                                Aliases = ["测试航线"]
                            }
                        ],
                    Ports =
                        [
                            new()
                            {
                                Value = "TEST PORT",
                                Aliases = ["测试港"]
                            }
                        ]
                });

            Assert.Equal("998", helpers.NormalizeCountryCode("测试国"));
            Assert.Equal("997", helpers.NormalizeAcdOriginCountryCode("测试货源"));
            Assert.Equal("996", helpers.NormalizeCurrencyCode("测试币"));
            Assert.Equal("TST", helpers.NormalizeCurrencyText("测试币"));
            Assert.Equal("9999", helpers.NormalizeTradeModeCode("测试模式"));
            Assert.Equal("BY TEST", helpers.NormalizeTransportMode("测试航线"));
            Assert.Equal("TEST PORT", helpers.NormalizePort("测试港"));
        }
    }
}

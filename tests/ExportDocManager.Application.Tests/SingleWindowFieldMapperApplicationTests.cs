using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowFieldMapperApplicationTests
    {
        [Fact]
        public void CustomsCooFieldMapper_ShouldNormalizeCoreFields()
        {
            var mapper = new CustomsCooFieldMapper();
            var snapshot = new CooSourceSnapshot
            {
                Invoice = new Invoice
                {
                    InvoiceNo = "INV-001",
                    DestinationCountry = "Indonesia",
                    Currency = "usd",
                    PortOfLoading = "ningbo",
                    PortOfDestination = "jakarta",
                    TransportMode = "海运",
                    SupervisionMode = "一般贸易",
                    ShipmentDate = new DateTime(2026, 4, 17),
                    InvoiceDate = new DateTime(2026, 4, 15),
                    TradeTerms = "fob ningbo",
                    ShippingMarks = "N/M",
                    SpecialTerms = "SPECIAL CLAUSE",
                    ExporterNameEN = "EXPORTER LTD.",
                    ExporterNameCN = "出口商有限公司",
                    ExporterAddressEN = "NINGBO, CHINA",
                    ExporterCreditCode = "91330200TEST000001",
                    CustomerNameEN = "BUYER LTD.",
                    CustomerAddressEN = "BUYER ADDRESS"
                },
                Items =
                [
                    new Item
                    {
                        StyleName = "POWER SUPPLY",
                        StyleNameCN = "电源",
                        HSCode = "850440",
                        Origin = "中国",
                        Quantity = 100,
                        UnitEN = "pcs",
                        UnitCN = "件",
                        Cartons = 10,
                        CtnUnitEN = "cartons",
                        UnitPrice = 10.2m,
                        TotalPrice = 1020m
                    }
                ],
                Customer = new Customer
                {
                    CustomerNameEN = "BUYER LTD.",
                    AddressEN = "BUYER ADDRESS",
                    Phone = "021-12345678",
                    Email = "buyer@example.com"
                },
                Exporter = new Exporter
                {
                    ExporterNameEN = "EXPORTER LTD.",
                    ExporterNameCN = "出口商有限公司",
                    AddressEN = "NINGBO, CHINA",
                    CreditCode = "91330200TEST000001",
                    ContactPerson = "Alice",
                    Phone = "0574-12345678"
                }
            };

            var result = mapper.Map(snapshot);
            var goods = Assert.Single(result.Goods);

            Assert.Equal("91330200TEST000001", result.CiqRegNo);
            Assert.Equal("360", result.DestCountryCode);
            Assert.Equal("1", result.TradeModeCode);
            Assert.Equal("FROM NINGBO TO JAKARTA BY SEA", result.TransDetails);
            Assert.Equal("FOB", result.PriceTerms);
            Assert.Equal("USD", result.Curr);
            Assert.Equal("156", result.OriCountryCode);
            Assert.Equal("CHINA", result.OriCountry);
            Assert.Equal($"EXPORTER LTD.{Environment.NewLine}NINGBO{Environment.NewLine}CHINA", result.Exporter);
            Assert.Equal("CTN", goods.PackUnit);
            Assert.Equal("PCS", goods.GoodsUnitE);
            Assert.Equal("156", goods.GoodsOriginCountry);
            Assert.Equal("CHINA", goods.GoodsOriginCountryEn);
        }

        [Fact]
        public void CustomsCooFieldMapper_ShouldPreferExistingGoodsBySourceIdentityBeforeLineNumber()
        {
            var mapper = new CustomsCooFieldMapper();
            var snapshot = new CooSourceSnapshot
            {
                Invoice = new Invoice
                {
                    InvoiceNo = "INV-IDENTITY",
                    ExporterNameCN = "出口商",
                    SupervisionMode = "一般贸易"
                },
                Items =
                [
                    new Item
                    {
                        Id = 42,
                        StyleNo = "SKU-42",
                        StyleName = "CURRENT GOODS",
                        StyleNameCN = "当前商品",
                        HSCode = "850440",
                        Quantity = 10,
                        UnitEN = "pcs",
                        Cartons = 1,
                        CtnUnitEN = "cartons",
                        TotalPrice = 100
                    }
                ],
                ExistingDocument = new CustomsCooDocument
                {
                    Items =
                    [
                        new CustomsCooItem
                        {
                            SourceItemId = 999,
                            SourceStyleNo = "OTHER-SKU",
                            GNo = 1,
                            HSCode = "WRONG-LINE"
                        },
                        new CustomsCooItem
                        {
                            SourceItemId = 42,
                            SourceStyleNo = "SKU-42",
                            GNo = 2,
                            HSCode = "LOCKED-BY-ID"
                        }
                    ]
                }
            };

            var goods = Assert.Single(mapper.Map(snapshot).Goods);

            Assert.Equal(1, goods.GNo);
            Assert.Equal("LOCKED-BY-ID", goods.HSCode);
        }

        [Fact]
        public void AgentConsignmentFieldMapper_ShouldNormalizeCodesAndFallbackDate()
        {
            var mapper = new AgentConsignmentFieldMapper();
            var snapshot = new AcdSourceSnapshot
            {
                Invoice = new Invoice
                {
                    InvoiceNo = "INV-002",
                    InvoiceDate = new DateTime(2026, 4, 17),
                    ShipmentDate = DateTime.MinValue,
                    Currency = "usd",
                    TotalAmount = 1020m,
                    TotalGrossWeight = 88m,
                    SupervisionMode = "一般贸易",
                    CustomsBrokerCode = "1234567890",
                    ExporterCreditCode = "91330200TEST000001",
                    ExporterCustomsCode = "3302961234",
                    SpecialTerms = "packed"
                },
                Items =
                [
                    new Item
                    {
                        StyleName = "POWER SUPPLY",
                        StyleNameCN = "电源",
                        HSCode = "850440",
                        Origin = "CN"
                    }
                ],
                Exporter = new Exporter
                {
                    CreditCode = "91330200TEST000001",
                    CustomsCode = "3302969999",
                    Phone = " 0574 1234 5678 "
                }
            };

            var result = mapper.Map(snapshot);

            Assert.Equal("3302961234", result.CopCusCode);
            Assert.Equal("3302961234", result.TradeCode);
            Assert.Equal("1234567890", result.AgentCode);
            Assert.Equal("20260417", result.IEDate);
            Assert.Equal("502", result.Curr);
            Assert.Equal("142", result.OriCountry);
            Assert.Equal("0110", result.TradeMode);
            Assert.Equal("057412345678", result.ConsignTele);
        }
    }
}

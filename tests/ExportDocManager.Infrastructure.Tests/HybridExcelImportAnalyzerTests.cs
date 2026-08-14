using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Data;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class HybridExcelImportAnalyzerTests
    {
        [Fact]
        public void PartyTextClassifier_DistinguishesBrandCompanyFromStreetAddress()
        {
            Assert.True(ExcelImportPartyTextClassifier.IsPlausiblePartyName("Reason Brand Inc"));
            Assert.True(ExcelImportPartyTextClassifier.LooksLikeCompanyName("Reason Brand Inc"));
            Assert.False(ExcelImportPartyTextClassifier.LooksLikePostalAddress("Reason Brand Inc"));
            Assert.True(ExcelImportPartyTextClassifier.IsPlausiblePartyName("New York City Trading Co., Ltd."));
            Assert.True(ExcelImportPartyTextClassifier.LooksLikeCompanyName("New York City Trading Co., Ltd."));
            Assert.True(ExcelImportPartyTextClassifier.LooksLikeCompanyName("FAME FASHION HOUSE LLC"));
            Assert.False(ExcelImportPartyTextClassifier.LooksLikeCompanyName(
                string.Join(' ', Enumerable.Repeat("ADDRESS", 10_000))));
            Assert.True(ExcelImportPartyTextClassifier.LooksLikePostalAddress(
                "3 WEST 35TH STREET 10th FL., New York, NY 10001"));
            Assert.False(ExcelImportPartyTextClassifier.IsPlausiblePartyName("4"));
            Assert.False(ExcelImportPartyTextClassifier.IsPlausiblePartyAddress("5"));
            Assert.Equal(0m, ExcelImportPartyTextClassifier.GetFieldQuality("NotifyPartyName", "4"));
            Assert.Equal(0m, ExcelImportPartyTextClassifier.GetFieldQuality("NotifyPartyAddress", "5"));
        }

        [Fact]
        public void NeedsDotNetFusion_RejectsHighConfidenceAddressMisclassifiedAsCustomerName()
        {
            var report = CreateCompleteReport(
                "3 WEST 35TH STREET 10th FL., New York, NY 10001",
                "3 WEST 35TH STREET 10th FL., New York, NY 10001");

            Assert.True(HybridExcelImportAnalyzer.NeedsDotNetFusion(report));
        }

        [Fact]
        public void MergeReports_PrefersSemanticallyValidCustomerNameOverHigherConfidenceAddress()
        {
            var external = CreateCompleteReport(
                "3 WEST 35TH STREET 10th FL., New York, NY 10001",
                "3 WEST 35TH STREET 10th FL., New York, NY 10001");
            external.AnalyzerId = "rust-calamine";
            var builtIn = new ExcelImportAnalysisReport
            {
                AnalyzerId = "dotnet-builtin",
                Fields =
                [
                    Field("CustomerNameEN", "Reason Brand Inc", 0.9m),
                    Field("CustomerAddressEN", "3 WEST 35TH STREET 10th FL., New York, NY 10001", 0.9m)
                ]
            };

            var merged = HybridExcelImportAnalyzer.MergeReports(external, builtIn);

            Assert.Contains(merged.Fields, field =>
                field.FieldKey == "CustomerNameEN" && field.Value == "Reason Brand Inc");
            Assert.Contains(merged.Fields, field =>
                field.FieldKey == "CustomerAddressEN"
                && field.Value == "3 WEST 35TH STREET 10th FL., New York, NY 10001");
            Assert.Equal("rust-calamine+dotnet-fusion", merged.AnalyzerId);
        }

        [Fact]
        public void MergeReports_KeepsExternalTableAndOnlyAddsNonConflictingFallbackColumns()
        {
            var external = CreateCompleteReport("Reason Brand Inc", "3 WEST 35TH STREET");
            external.ItemTable = new ExcelImportItemTableAnalysis
            {
                WorksheetName = "金海江数据页",
                HeaderRow = 21,
                HeaderDepth = 2,
                DataStartRow = 23,
                Confidence = 0.9m,
                Columns = new ExcelImportItemColumnAnalysis
                {
                    PoNumberCol = 3,
                    StyleNoCol = 4,
                    StyleNameCol = 5,
                    QuantityCol = 6,
                    CartonsCol = 7
                }
            };
            var builtIn = new ExcelImportAnalysisReport
            {
                ItemTable = new ExcelImportItemTableAnalysis
                {
                    WorksheetName = "金海江数据页",
                    HeaderRow = 19,
                    HeaderDepth = 3,
                    DataStartRow = 23,
                    Confidence = 0.95m,
                    Columns = new ExcelImportItemColumnAnalysis
                    {
                        PoNumberCol = 3,
                        StyleNoCol = 4,
                        StyleNameCol = 5,
                        QuantityCol = 6,
                        CartonsCol = 7,
                        HSCodeCol = 3,
                        UnitPriceCol = 11,
                        TotalPriceCol = 12
                    }
                }
            };

            var merged = HybridExcelImportAnalyzer.MergeReports(external, builtIn);

            Assert.NotNull(merged.ItemTable);
            Assert.Equal(21, merged.ItemTable.HeaderRow);
            Assert.Equal(2, merged.ItemTable.HeaderDepth);
            Assert.Equal(0, merged.ItemTable.Columns.HSCodeCol);
            Assert.Equal(11, merged.ItemTable.Columns.UnitPriceCol);
            Assert.Equal(12, merged.ItemTable.Columns.TotalPriceCol);
        }

        private static ExcelImportAnalysisReport CreateCompleteReport(
            string customerName,
            string customerAddress)
        {
            return new ExcelImportAnalysisReport
            {
                Confidence = 0.98m,
                ItemTable = new ExcelImportItemTableAnalysis
                {
                    Confidence = 0.95m,
                    Columns = new ExcelImportItemColumnAnalysis
                    {
                        StyleNoCol = 1,
                        QuantityCol = 2
                    }
                },
                Fields =
                [
                    Field("InvoiceNo", "2026YH027"),
                    Field("CustomerNameEN", customerName),
                    Field("CustomerAddressEN", customerAddress),
                    Field("ExporterNameEN", "NINGBO BRIDGE IMP. & EXP. CO., LTD."),
                    Field("ExporterAddressEN", "NO.668 EAST BAIZHANG ROAD, NINGBO, CHINA"),
                    Field("PortOfLoading", "SHANGHAI"),
                    Field("PortOfDestination", "NEW YORK")
                ]
            };
        }

        private static ExcelImportFieldAnalysis Field(
            string fieldKey,
            string value,
            decimal confidence = 0.95m)
        {
            return new ExcelImportFieldAnalysis
            {
                FieldKey = fieldKey,
                Value = value,
                Confidence = confidence
            };
        }
    }
}

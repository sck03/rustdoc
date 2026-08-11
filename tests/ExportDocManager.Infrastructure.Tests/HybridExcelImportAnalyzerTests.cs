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
            Assert.True(ExcelImportPartyTextClassifier.LooksLikePostalAddress(
                "3 WEST 35TH STREET 10th FL., New York, NY 10001"));
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

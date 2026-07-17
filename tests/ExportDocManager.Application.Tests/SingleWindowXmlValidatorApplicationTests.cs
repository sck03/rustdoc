using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowXmlValidatorApplicationTests
    {
        [Fact]
        public void ValidateForBuild_ShouldReportMissingCustomsCooRequiredFields()
        {
            var validator = new SingleWindowXmlValidator();

            var errors = validator.ValidateForBuild(
                SingleWindowBusinessType.CustomsCoo,
                new CooMappedDocument());

            Assert.Contains(errors, item => item.Contains("原产地证编号(CertNo)不能为空。", StringComparison.Ordinal));
            Assert.Contains(errors, item => item.Contains("出口商(Exporter)不能为空。", StringComparison.Ordinal));
        }

        [Fact]
        public void CustomsCooOriginCriteriaCatalog_ShouldKeepGspHeadingNormalization()
        {
            string normalized = CustomsCooOriginCriteriaCatalog.NormalizeOriginCriteriaRefInput(
                certType: "G",
                originCriteria: "W",
                hsCode: "620342",
                value: string.Empty);

            Assert.Equal("62.03", normalized);
        }

        [Theory]
        [InlineData("C", "")]
        [InlineData("G", "F,P,PK,W,Y")]
        [InlineData("A", "WO,WP,PSR")]
        [InlineData("F", "WO,WP,PSR")]
        [InlineData("RC", "CR,CTC,PE,RVC,WO")]
        public void CustomsCooOriginCriteriaCatalog_ShouldExposeOfficialCommonCertificateOptions(
            string certType,
            string expectedCsv)
        {
            var actual = CustomsCooOriginCriteriaCatalog.GetOriginCriteriaOptions(certType)
                .Select(option => option.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            Assert.Equal(
                expectedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
                actual);
        }

        [Fact]
        public void SingleWindowFieldValidationHelper_ShouldKeepDecimalFormatting()
        {
            Assert.Equal("12.3", SingleWindowFieldValidationHelper.FormatDecimal(12.3000m, 4));
            Assert.Equal(string.Empty, SingleWindowFieldValidationHelper.FormatDecimal(0m, 2));
        }
    }
}

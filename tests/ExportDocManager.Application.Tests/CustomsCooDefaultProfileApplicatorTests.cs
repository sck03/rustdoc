using ExportDocManager.Models;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class CustomsCooDefaultProfileApplicatorTests
    {
        [Fact]
        public void Apply_ShouldFillConfiguredDefaultsAndRemoveSatisfiedWarnings()
        {
            var document = new CooMappedDocument
            {
                Warnings =
                [
                    "申报员姓名(ApplName)缺失。",
                    "签证机构代码(OrgCode)缺失。",
                    "出口商(Exporter)缺失。"
                ]
            };
            var defaults = new CustomsCooDefaultProfile
            {
                ApplName = "Alice",
                OrgCode = "1234"
            };

            CustomsCooDefaultProfileApplicator.Apply(document, defaults);

            Assert.Equal("Alice", document.ApplName);
            Assert.Equal("1234", document.OrgCode);
            Assert.DoesNotContain(document.Warnings, item => item.Contains("ApplName", StringComparison.Ordinal));
            Assert.DoesNotContain(document.Warnings, item => item.Contains("OrgCode", StringComparison.Ordinal));
            Assert.Contains(document.Warnings, item => item.Contains("Exporter", StringComparison.Ordinal));
        }
    }
}

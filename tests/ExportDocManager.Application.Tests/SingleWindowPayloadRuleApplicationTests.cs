using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowPayloadRuleApplicationTests
    {
        [Fact]
        public void CustomsCooTextFormatter_ShouldEncodeMultilineWithSlashN()
        {
            string value = $"EXPORTER LTD.{Environment.NewLine}NINGBO";

            string encoded = CustomsCooTextFormatter.EncodeXmlMultiline(value);
            string decoded = CustomsCooTextFormatter.DecodeXmlMultiline(encoded);

            Assert.Equal("EXPORTER LTD./nNINGBO", encoded);
            Assert.Equal(value, decoded);
        }

        [Theory]
        [InlineData("商业发票.pdf", "1", "PDF")]
        [InlineData("运输提单.docx", "3", "DOCX")]
        [InlineData("unknown", "7", "")]
        public void PayloadFileNameHelper_ShouldInferAttachmentMetadata(string fileName, string expectedFileType, string expectedDocType)
        {
            Assert.Equal(expectedFileType, SingleWindowPayloadFileNameHelper.ResolveCooAttachmentFileType(fileName));
            Assert.Equal(expectedDocType, SingleWindowPayloadFileNameHelper.ResolveDocType(fileName));
        }
    }
}

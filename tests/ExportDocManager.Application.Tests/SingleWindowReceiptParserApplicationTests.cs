using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowReceiptParserApplicationTests
    {
        private readonly SingleWindowReceiptParser _parser = new();

        [Fact]
        public void Parse_CustomsCooReceipt_ShouldReturnBusinessReceipt()
        {
            const string xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <Receipt xmlns="http://www.w3.org/2000/09/xmldsig#">
                  <CertNo>RC220123456780001</CertNo>
                  <ReceiveTime>2026-04-16 10:00:00</ReceiveTime>
                  <Channel>6</Channel>
                  <Note>业务回执</Note>
                  <SendTime>2026-04-16 09:59:00</SendTime>
                  <CusRespData>
                    <RepType>5</RepType>
                    <RepCode>0000</RepCode>
                    <RepAddMsg>审核通过</RepAddMsg>
                  </CusRespData>
                </Receipt>
                """;

            var result = _parser.Parse(SingleWindowBusinessType.CustomsCoo, xml, "receipt.xml");

            Assert.Equal(SingleWindowReceiptKind.CustomsCooBusinessReceipt, result.ReceiptKind);
            Assert.Equal("RC220123456780001", result.ReferenceNo);
            Assert.Equal("0000", result.ReceiptCode);
            Assert.Equal(SingleWindowReceiptBusinessStatus.Approved, result.BusinessStatus);
        }

        [Fact]
        public void Parse_AgentConsignmentImportResponse_ShouldReturnImportResponse()
        {
            const string xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <ImportAgrResponse xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <ResponseInfo>
                    <ResponseCode>0</ResponseCode>
                    <ResponseMessage>成功</ResponseMessage>
                  </ResponseInfo>
                  <ConsignNo>195200016172325</ConsignNo>
                </ImportAgrResponse>
                """;

            var result = _parser.Parse(SingleWindowBusinessType.AgentConsignment, xml, "acd-response.xml");

            Assert.Equal(SingleWindowReceiptKind.AgentConsignmentImportResponse, result.ReceiptKind);
            Assert.Equal("195200016172325", result.ReferenceNo);
            Assert.Equal("0", result.ReceiptCode);
            Assert.Equal(SingleWindowReceiptBusinessStatus.Accepted, result.BusinessStatus);
        }
    }
}

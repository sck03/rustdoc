using System.Xml.Linq;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Infrastructure.Tests
{
    public class SingleWindowPayloadGeneratorInfrastructureTests
    {
        [Fact]
        public void BuildCertificateXml_ShouldIncludeRcepInvoiceNode()
        {
            var generator = new CustomsCooXmlPayloadGenerator();
            var document = new CooMappedDocument
            {
                CertType = "RC",
                CertNo = "RC260001",
                InvNo = "INV-001",
                InvDate = "2026-04-29",
                Curr = "USD",
                TotalAmt = "1000",
                PriceTerms = "FOB"
            };

            var result = generator.BuildCertificateXml(document);
            var xml = XDocument.Parse(result.Content);
            XNamespace ns = "http://www.w3.org/2000/09/xmldsig#";

            var oriInv = xml.Root?
                .Element(ns + "OriInvs")?
                .Element(ns + "OriInv");

            Assert.NotNull(oriInv);
            Assert.Equal("INV-001", oriInv.Element(ns + "InvNo")?.Value);
            Assert.Equal("USD", oriInv.Element(ns + "Curr")?.Value);
        }

        [Fact]
        public void BuildAttachmentXmls_ShouldReadAttachmentFileContent()
        {
            string attachmentPath = Path.GetTempFileName();
            File.WriteAllText(attachmentPath, "invoice");

            try
            {
                var generator = new CustomsCooXmlPayloadGenerator();
                var document = new CooMappedDocument
                {
                    CertNo = "RC260188",
                    CertType = "RC",
                    AplRegNo = "91330200TEST000001",
                    CiqRegNo = "91330200TEST000001",
                    Attachments =
                    [
                        new SingleWindowAttachmentSource
                        {
                            FileName = "商业发票.pdf",
                            FilePath = attachmentPath
                        }
                    ]
                };

                var payload = Assert.Single(generator.BuildAttachmentXmls(document));
                var xml = XDocument.Parse(payload.Content);

                Assert.Equal("1", xml.Root?.Element("FileType")?.Value);
                Assert.Equal(Convert.ToBase64String(File.ReadAllBytes(attachmentPath)), xml.Root?.Element("FileContent")?.Value);
            }
            finally
            {
                if (File.Exists(attachmentPath))
                {
                    File.Delete(attachmentPath);
                }
            }
        }
    }
}

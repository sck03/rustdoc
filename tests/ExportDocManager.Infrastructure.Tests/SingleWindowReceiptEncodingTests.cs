using System.Text;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Services.Time;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SingleWindowReceiptEncodingTests
    {
        private const string Xml = "<?xml version=\"1.0\"?><Receipt><CertNo>证书-001</CertNo><Note>已接收</Note></Receipt>";

        [Fact]
        public void DecodeReceiptContent_ShouldReadUtf8WithOrWithoutBom()
        {
            var encodings = new Encoding[]
            {
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true)
            };

            foreach (Encoding encoding in encodings)
            {
                Assert.Equal(Xml, ManualImportClientBridge.DecodeReceiptContent(encoding.GetBytes(Xml)));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DecodeReceiptContent_ShouldReadUtf16WithOrWithoutBom(bool bigEndian)
        {
            Encoding noBomEncoding = new UnicodeEncoding(
                bigEndian,
                byteOrderMark: false,
                throwOnInvalidBytes: true);
            Encoding bomEncoding = new UnicodeEncoding(
                bigEndian,
                byteOrderMark: true,
                throwOnInvalidBytes: true);

            Assert.Equal(Xml, ManualImportClientBridge.DecodeReceiptContent(noBomEncoding.GetBytes(Xml)));
            Assert.Equal(Xml, ManualImportClientBridge.DecodeReceiptContent(bomEncoding.GetBytes(Xml)));
        }

        [Fact]
        public void DecodeReceiptContent_ShouldIgnoreLeadingUtf16WhitespaceWithoutBom()
        {
            const string xmlWithWhitespace = "  \r\n\t" + Xml;
            byte[] bytes = new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false,
                throwOnInvalidBytes: true).GetBytes(xmlWithWhitespace);

            Assert.Equal(xmlWithWhitespace, ManualImportClientBridge.DecodeReceiptContent(bytes));
        }

        [Fact]
        public void DecodeReceiptContent_ShouldRejectInvalidUtf8()
        {
            byte[] invalidUtf8 = [0x3C, 0xFF, 0x3E];

            Assert.Throws<DecoderFallbackException>(() =>
                ManualImportClientBridge.DecodeReceiptContent(invalidUtf8));
        }

        [Fact]
        public void ReceiptParser_ShouldRejectMalformedXmlAfterDecoding()
        {
            string decoded = ManualImportClientBridge.DecodeReceiptContent(
                Encoding.UTF8.GetBytes("<Receipt><CertNo>broken"));
            var parser = new SingleWindowReceiptParser(new FixedBusinessClock());

            Assert.Throws<System.Xml.XmlException>(() =>
                parser.Parse(
                    SingleWindowBusinessType.CustomsCoo,
                    decoded));
        }

        private sealed class FixedBusinessClock : IBusinessClock
        {
            public string TimeZoneId => "Asia/Shanghai";

            public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public DateTimeOffset Now => UtcNow.AddHours(8);

            public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

            public DateTimeOffset TodayValidUntilUtc => UtcNow.AddDays(1);

            public DateTimeOffset InterpretLocal(DateTime localTime)
            {
                return new DateTimeOffset(localTime, TimeSpan.FromHours(8));
            }
        }
    }
}

using ExportDocManager.Services.Security;

namespace ExportDocManager.Infrastructure.Tests
{
    public class LicenseSignatureVerifierTests
    {
        private const string PublicTestVector =
            "EDM2-eyJ2IjoyLCJtaWQiOiJwdWJsaWN0ZXN0bWFjaGluZSIsImV4cCI6MTkyNDk5MTk5OSwiaWF0IjoxNzY3MjI1NjAwLCJsaWQiOiJwdWJsaWMtdGVzdC12ZWN0b3IiLCJlZCI6IlByb2Zlc3Npb25hbCJ9.yC3VpnNASvm4batEiBxVN7GoexTIrLQHL-dNapTXg2J4-r3V_PnNEtlpZ13vkNc5lXNVlt7EokWghKNkFn-34Q";

        [Fact]
        public void TryValidate_ShouldAcceptPublicVectorAndRejectMachineMismatch()
        {
            var verifier = new EcdsaLicenseSignatureVerifier();

            bool accepted = verifier.TryValidate(
                "PUBLIC-TEST-MACHINE",
                PublicTestVector,
                out DateTime expireDate);
            bool rejected = verifier.TryValidate(
                "ANOTHER-MACHINE",
                PublicTestVector,
                out _);

            Assert.True(accepted);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1924991999).LocalDateTime, expireDate);
            Assert.False(rejected);
        }
    }
}

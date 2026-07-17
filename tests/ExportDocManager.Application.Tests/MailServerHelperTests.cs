using ExportDocManager.Utils;

namespace ExportDocManager.Application.Tests
{
    public class MailServerHelperTests
    {
        [Theory]
        [InlineData("user@qq.com", "smtp.qq.com", 465, true)]
        [InlineData("USER@GMAIL.COM", "smtp.gmail.com", 587, true)]
        [InlineData("user@outlook.com", "smtp.office365.com", 587, true)]
        [InlineData("user@bridgegroup.cn", "smtpcom.263xmail.com", 465, true)]
        public void GetServerConfig_ShouldReturnKnownProviderDefaults(
            string email,
            string expectedHost,
            int expectedPort,
            bool expectedSsl)
        {
            var result = MailServerHelper.GetServerConfig(email);

            Assert.NotNull(result);
            Assert.Equal(expectedHost, result.Value.SmtpHost);
            Assert.Equal(expectedPort, result.Value.Port);
            Assert.Equal(expectedSsl, result.Value.Ssl);
        }

        [Theory]
        [InlineData("user@company.com", "smtp.company.com")]
        [InlineData("user@sub.example.cn", "smtp.sub.example.cn")]
        public void GetServerConfig_ShouldInferEnterpriseMailServer(string email, string expectedHost)
        {
            var result = MailServerHelper.GetServerConfig(email);

            Assert.NotNull(result);
            Assert.Equal(expectedHost, result.Value.SmtpHost);
            Assert.Equal(465, result.Value.Port);
            Assert.True(result.Value.Ssl);
        }

        [Theory]
        [InlineData("user@263.net", "smtp.263.net")]
        [InlineData("user@mail.263.net.cn", "smtp.263.net")]
        [InlineData("user@corp.263xmail.com", "smtpcom.263xmail.com")]
        public void GetServerConfig_ShouldKeepSpecial263Rules(string email, string expectedHost)
        {
            var result = MailServerHelper.GetServerConfig(email);

            Assert.NotNull(result);
            Assert.Equal(expectedHost, result.Value.SmtpHost);
            Assert.Equal(465, result.Value.Port);
            Assert.True(result.Value.Ssl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("missing-at-symbol")]
        [InlineData("a@b@c")]
        public void GetServerConfig_ShouldReturnNullForInvalidEmail(string email)
        {
            Assert.Null(MailServerHelper.GetServerConfig(email));
        }
    }
}

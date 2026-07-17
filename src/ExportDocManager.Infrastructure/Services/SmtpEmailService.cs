using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using ExportDocManager.Models;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Infrastructure
{
    public class SmtpEmailService : IEmailService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(ISettingsService settingsService, ILogger<SmtpEmailService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }
        
        private EmailConfig Config => _settingsService.Settings?.Email ?? new EmailConfig();

        public async Task SendEmailAsync(string to, string subject, string body, List<string> attachments = null)
        {
            var config = Config;
            var smtpHost = RequireValue(config.SmtpHost, "SMTP 服务器未配置，请先到“系统设置 > 邮件设置”中填写。");
            var fromAddress = ResolveFromAddress(config);
            var recipientAddress = RequireValue(to, "收件人地址不能为空。");

            using (var message = new MailMessage())
            {
                message.From = CreateMailAddress(fromAddress, config.FromDisplayName);
                message.To.Add(CreateMailAddress(recipientAddress));
                message.Subject = subject ?? string.Empty;
                message.Body = body ?? string.Empty;
                message.IsBodyHtml = true;

                if (attachments != null)
                {
                    foreach (var path in attachments
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (System.IO.File.Exists(path))
                        {
                            message.Attachments.Add(new Attachment(path));
                        }
                    }
                }

                using (var client = CreateSmtpClient(config, smtpHost))
                {
                    try
                    {
                        await client.SendMailAsync(message);
                        _logger.LogInformation("Email sent to {Recipient} successfully.", recipientAddress);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send email to {Recipient}", recipientAddress);
                        throw;
                    }
                }
            }
        }

        public async Task TestConnectionAsync(EmailConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var smtpHost = RequireValue(config.SmtpHost, "SMTP 服务器未配置，请先填写后再测试。");
            var fromAddress = ResolveFromAddress(config);

            using (var client = CreateSmtpClient(config, smtpHost))
            using (var message = new MailMessage())
            {
                message.From = CreateMailAddress(fromAddress, config.FromDisplayName);
                message.To.Add(CreateMailAddress(fromAddress));
                message.Subject = "ExportDocManager SMTP Test";
                message.Body = "This is a test email from ExportDocManager.";
                message.IsBodyHtml = false;

                await client.SendMailAsync(message);
            }
        }

        private static SmtpClient CreateSmtpClient(EmailConfig config, string smtpHost)
        {
            var client = new SmtpClient(smtpHost, config.SmtpPort)
            {
                EnableSsl = config.EnableSsl,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(config.UserName) && !string.IsNullOrWhiteSpace(config.Password))
            {
                client.Credentials = new NetworkCredential(config.UserName.Trim(), config.Password);
            }

            return client;
        }

        private static MailAddress CreateMailAddress(string address, string displayName = null)
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? new MailAddress(address)
                : new MailAddress(address, displayName.Trim());
        }

        private static string ResolveFromAddress(EmailConfig config)
        {
            var fromAddress = !string.IsNullOrWhiteSpace(config.FromAddress)
                ? config.FromAddress
                : config.UserName;

            return RequireValue(fromAddress, "发件人地址未配置，请先填写发件人地址或用户名/邮箱。");
        }

        private static string RequireValue(string value, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(errorMessage);
            }

            return value.Trim();
        }
    }
}

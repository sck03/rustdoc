using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Infrastructure
{
    public class SmtpEmailService : IEmailService
    {
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ConnectionTestTimeout = TimeSpan.FromSeconds(30);

        private readonly ISettingsService _settingsService;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(ISettingsService settingsService, ILogger<SmtpEmailService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }
        
        private EmailConfig Config => _settingsService.Settings?.Email ?? new EmailConfig();

        public async Task SendEmailAsync(
            string to,
            string subject,
            string body,
            List<string>? attachments = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var config = Config;
            var smtpHost = RequireValue(config.SmtpHost, "SMTP 服务器未配置，请先到“系统设置 > 邮件设置”中填写。");
            var fromAddress = ResolveFromAddress(config);
            var recipientAddress = RequireValue(to, "收件人地址不能为空。");
            var attachmentPaths = EmailAttachmentPolicy.ValidateAndNormalize(attachments ?? []);

            using var message = new MailMessage
            {
                From = CreateMailAddress(fromAddress, config.FromDisplayName),
                Subject = subject ?? string.Empty,
                Body = body ?? string.Empty,
                IsBodyHtml = true
            };
            message.To.Add(CreateMailAddress(recipientAddress));

            long attachedBytes = 0;
            foreach (string path in attachmentPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                try
                {
                    if (stream.Length > EmailAttachmentPolicy.MaximumSingleAttachmentBytes)
                    {
                        throw new ExportDocManager.Utils.PayloadLimitExceededException(
                            EmailAttachmentPolicy.MaximumSingleAttachmentBytes);
                    }
                    if (attachedBytes > EmailAttachmentPolicy.MaximumTotalAttachmentBytes - stream.Length)
                    {
                        throw new ExportDocManager.Utils.PayloadLimitExceededException(
                            EmailAttachmentPolicy.MaximumTotalAttachmentBytes);
                    }

                    message.Attachments.Add(new Attachment(stream, Path.GetFileName(path)));
                    attachedBytes += stream.Length;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }

            using var client = CreateSmtpClient(config, smtpHost, SendTimeout);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(SendTimeout);
            try
            {
                await client.SendMailAsync(message, timeoutSource.Token).ConfigureAwait(false);
                _logger.LogInformation("Email sent to {Recipient} successfully.", recipientAddress);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Sending email to {Recipient} timed out.", recipientAddress);
                throw new ServiceTimeoutException("邮件发送超时，请稍后重试。", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Recipient}", recipientAddress);
                throw MapSmtpInfrastructureFailure(ex);
            }
        }

        public async Task TestConnectionAsync(
            EmailConfig config,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            cancellationToken.ThrowIfCancellationRequested();

            var smtpHost = RequireValue(config.SmtpHost, "SMTP 服务器未配置，请先填写后再测试。");
            var fromAddress = ResolveFromAddress(config);

            using (var client = CreateSmtpClient(config, smtpHost, ConnectionTestTimeout))
            using (var message = new MailMessage())
            {
                message.From = CreateMailAddress(fromAddress, config.FromDisplayName);
                message.To.Add(CreateMailAddress(fromAddress));
                message.Subject = "ExportDocManager SMTP Test";
                message.Body = "This is a test email from ExportDocManager.";
                message.IsBodyHtml = false;

                try
                {
                    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutSource.CancelAfter(ConnectionTestTimeout);
                    await client.SendMailAsync(message, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ServiceTimeoutException("SMTP 连接测试超时，请稍后重试。", ex);
                }
                catch (Exception ex)
                {
                    throw MapSmtpInfrastructureFailure(ex);
                }
            }
        }

        private static SmtpClient CreateSmtpClient(
            EmailConfig config,
            string smtpHost,
            TimeSpan timeout)
        {
            var client = new SmtpClient(smtpHost, config.SmtpPort)
            {
                EnableSsl = config.EnableSsl,
                UseDefaultCredentials = false,
                Timeout = checked((int)timeout.TotalMilliseconds)
            };

            if (!string.IsNullOrWhiteSpace(config.UserName) && !string.IsNullOrWhiteSpace(config.Password))
            {
                client.Credentials = new NetworkCredential(config.UserName.Trim(), config.Password);
            }

            return client;
        }

        private static MailAddress CreateMailAddress(string address, string? displayName = null)
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
                throw new ServiceValidationException(errorMessage);
            }

            return value.Trim();
        }

        private static Exception MapSmtpInfrastructureFailure(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return exception;
            }

            return exception is InfrastructureServiceException
                ? exception
                : new InfrastructureServiceException(
                    "邮件服务暂时不可用，请检查 SMTP 地址、端口、TLS 和网络连接后重试。",
                    exception);
        }
    }
}

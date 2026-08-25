using System.Net;
using System.Text.RegularExpressions;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// SMTP delivery backed by MailKit's cancellable, cross-platform client.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ConnectionTestTimeout = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settingsService;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        ISettingsService settingsService,
        ILogger<SmtpEmailService> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private EmailConfig Config => _settingsService.Settings.Email;

    public async Task SendEmailAsync(
        string to,
        string subject,
        string body,
        List<string>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = Config;
        string smtpHost = RequireValue(config.SmtpHost, "SMTP 服务器未配置，请先到“系统设置 > 邮件设置”中填写。");
        string fromAddress = ResolveFromAddress(config);
        string recipientAddress = RequireValue(to, "收件人地址不能为空。");
        var attachmentPaths = EmailAttachmentPolicy.ValidateAndNormalize(attachments ?? []);
        var message = BuildMessage(
            fromAddress,
            config.FromDisplayName,
            recipientAddress,
            subject,
            body,
            attachmentPaths,
            isHtml: true);

        try
        {
            await SendMessageAsync(config, smtpHost, message, SendTimeout, cancellationToken)
                .ConfigureAwait(false);
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
        finally
        {
            message.Dispose();
        }
    }

    public async Task TestConnectionAsync(
        EmailConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();

        string smtpHost = RequireValue(config.SmtpHost, "SMTP 服务器未配置，请先填写后再测试。");
        string fromAddress = ResolveFromAddress(config);
        using var message = BuildMessage(
            fromAddress,
            config.FromDisplayName,
            fromAddress,
            "ExportDocManager SMTP Test",
            "This is a test email from ExportDocManager.",
            [],
            isHtml: false);

        try
        {
            await SendMessageAsync(config, smtpHost, message, ConnectionTestTimeout, cancellationToken)
                .ConfigureAwait(false);
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

    private static async Task SendMessageAsync(
        EmailConfig config,
        string smtpHost,
        MimeMessage message,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using var client = new SmtpClient
        {
            Timeout = checked((int)timeout.TotalMilliseconds)
        };

        try
        {
            await client.ConnectAsync(
                    smtpHost,
                    config.SmtpPort,
                    ResolveSocketOptions(config),
                    timeoutSource.Token)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(config.UserName)
                && !string.IsNullOrWhiteSpace(config.Password))
            {
                await client.AuthenticateAsync(
                        new NetworkCredential(config.UserName.Trim(), config.Password),
                        timeoutSource.Token)
                    .ConfigureAwait(false);
            }

            await client.SendAsync(message, timeoutSource.Token).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, timeoutSource.Token).ConfigureAwait(false);
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the original delivery failure; the client is disposed below.
                }
            }
        }
    }

    private static MimeMessage BuildMessage(
        string fromAddress,
        string? fromDisplayName,
        string recipientAddress,
        string? subject,
        string? body,
        IReadOnlyList<string> attachmentPaths,
        bool isHtml)
    {
        var message = new MimeMessage();
        message.From.Add(CreateMailboxAddress(fromAddress, fromDisplayName));
        message.To.Add(CreateMailboxAddress(recipientAddress));
        message.Subject = subject ?? string.Empty;

        var builder = new BodyBuilder();
        if (isHtml)
        {
            builder.TextBody = ConvertHtmlToPlainText(body);
            builder.HtmlBody = body ?? string.Empty;
        }
        else
        {
            builder.TextBody = body ?? string.Empty;
        }

        foreach (string path in attachmentPaths)
        {
            builder.Attachments.Add(path);
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static string ConvertHtmlToPlainText(string? html)
    {
        string value = html ?? string.Empty;
        value = Regex.Replace(value, "<\\s*br\\s*/?\\s*>", "\\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        return WebUtility.HtmlDecode(value).Trim();
    }

    private static MailboxAddress CreateMailboxAddress(string address, string? displayName = null)
    {
        try
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? MailboxAddress.Parse(address)
                : new MailboxAddress(displayName.Trim(), address);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ServiceValidationException($"邮箱地址无效：{ex.Message}", ex);
        }
    }

    private static SecureSocketOptions ResolveSocketOptions(EmailConfig config) =>
        !config.EnableSsl
            ? SecureSocketOptions.None
            : config.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

    private static string ResolveFromAddress(EmailConfig config)
    {
        var fromAddress = !string.IsNullOrWhiteSpace(config.FromAddress)
            ? config.FromAddress
            : config.UserName;

        return RequireValue(fromAddress, "发件人地址未配置，请先填写发件人地址或用户名/邮箱。");
    }

    private static string RequireValue(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ServiceValidationException(errorMessage);
        }

        return value.Trim();
    }

    private static Exception MapSmtpInfrastructureFailure(Exception exception)
    {
        if (exception is OperationCanceledException or ServiceException)
        {
            return exception;
        }

        return new InfrastructureServiceException(
            "邮件服务暂时不可用，请检查 SMTP 地址、端口、TLS 和网络连接后重试。",
            exception);
    }
}

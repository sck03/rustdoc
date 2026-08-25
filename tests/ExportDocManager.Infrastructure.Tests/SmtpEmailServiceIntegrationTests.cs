using System.Net;
using System.Net.Sockets;
using System.Text;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class SmtpEmailServiceIntegrationTests
{
    [Fact]
    public async Task SendEmailAsync_ShouldEmitPlainTextAndHtmlMimeAlternatives()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> server = ReceiveMessageAsync(listener, timeout.Token);
        var settings = new StubSettingsService(new AppSettings
        {
            Email = new EmailConfig
            {
                SmtpHost = "127.0.0.1",
                SmtpPort = port,
                EnableSsl = false,
                FromAddress = "sender@example.com",
                FromDisplayName = "ExportDocManager"
            }
        });
        var service = new SmtpEmailService(settings, NullLogger<SmtpEmailService>.Instance);

        await service.SendEmailAsync(
            "buyer@example.com",
            "Invoice documents",
            "<p>Hello <strong>World</strong></p>",
            cancellationToken: timeout.Token);

        string mime = await server.WaitAsync(timeout.Token);
        Assert.Contains("multipart/alternative", mime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/plain", mime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/html", mime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello World", mime, StringComparison.Ordinal);
    }

    private static async Task<string> ReceiveMessageAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\r\n"
        };
        await writer.WriteLineAsync("220 localhost ESMTP ready");

        var message = new StringBuilder();
        bool readingData = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (readingData)
            {
                if (line == ".")
                {
                    readingData = false;
                    await writer.WriteLineAsync("250 2.0.0 queued");
                }
                else
                {
                    message.AppendLine(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
                }
                continue;
            }

            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250-localhost");
                await writer.WriteLineAsync("250 SIZE 52428800");
            }
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                readingData = true;
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 2.0.0 bye");
                return message.ToString();
            }
            else
            {
                await writer.WriteLineAsync("250 2.0.0 ok");
            }
        }

        return message.ToString();
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Settings { get; } = settings;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

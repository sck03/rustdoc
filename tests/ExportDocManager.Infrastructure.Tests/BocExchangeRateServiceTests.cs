using System.Net;
using ExportDocManager.Models;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class BocExchangeRateServiceTests
{
    [Theory]
    [InlineData("http://www.boc.cn/sourcedb/whpj/")]
    [InlineData("https://127.0.0.1/rates")]
    [InlineData("https://10.0.0.8/rates")]
    [InlineData("https://100.64.0.8/rates")]
    [InlineData("https://[2001:db8::8]/rates")]
    [InlineData("https://user:password@rates.example/rates")]
    [InlineData("https://rates.example/rates?tenant=internal")]
    public void EndpointPolicy_ShouldRejectUnsafeConfiguredSources(string value)
    {
        Assert.Throws<ServiceValidationException>(() => ExchangeRateEndpointPolicy.Normalize(value));
    }

    [Fact]
    public async Task EndpointPolicy_ShouldRejectHostWhenAnyResolvedAddressIsPrivate()
    {
        Uri endpoint = ExchangeRateEndpointPolicy.Normalize("https://rates.example/public");

        var error = await Assert.ThrowsAsync<ServiceValidationException>(() =>
            ExchangeRateEndpointPolicy.ValidatePublicHostAsync(
                endpoint,
                (_, _) => Task.FromResult(new[]
                {
                    IPAddress.Parse("93.184.216.34"),
                    IPAddress.Parse("169.254.169.254")
                }),
                CancellationToken.None));

        Assert.Contains("不可访问网络", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionConnectCallback_ShouldBlockPrivateTransportTarget()
    {
        bool transportReached = false;
        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await ExchangeRateEndpointPolicy.ConnectPublicHostTransportAsync(
                new DnsEndPoint("127.0.0.1", 443),
                (_, _) => throw new InvalidOperationException("Literal addresses must not use DNS."),
                (_, _, _) =>
                {
                    transportReached = true;
                    throw new InvalidOperationException("Blocked addresses must not reach the socket transport.");
                },
                CancellationToken.None));

        Assert.IsType<ServiceValidationException>(error.InnerException);
        Assert.False(transportReached);
    }

    [Fact]
    public async Task Service_ShouldValidateEveryRedirectAndParseBoundedHtml()
    {
        var handler = new SequenceHandler(
            request => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("/final", UriKind.Relative) }
            },
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <table><tr><td>美元</td><td>720</td><td>718</td><td>725</td><td>726</td><td>723</td><td>2026-08-08 10:00:00</td></tr></table>
                    """)
            });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var rates = await service.GetExchangeRatesAsync();

        var rate = Assert.Single(rates);
        Assert.Equal("美元", rate.CurrencyName);
        Assert.Equal(7.2m, rate.BuyingRate);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("https://rates.example/final", handler.RequestUris[1].ToString());
    }

    [Fact]
    public async Task Service_ShouldStopBeforeRedirectingIntoPrivateNetwork()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://metadata.example/latest") }
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (host, _) => Task.FromResult(new[]
            {
                host == "metadata.example"
                    ? IPAddress.Parse("169.254.169.254")
                    : IPAddress.Parse("93.184.216.34")
            }));

        await Assert.ThrowsAsync<ServiceValidationException>(() => service.GetExchangeRatesAsync());
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Service_ShouldHonorCallerCancellationBeforeNetworkAccess()
    {
        var handler = new SequenceHandler(_ => throw new InvalidOperationException("HTTP should not be reached."));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetExchangeRatesAsync(cancellation.Token));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Service_ShouldClassifyInternalTimeout()
    {
        using var client = new HttpClient(new BlockingHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }),
            TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<ServiceTimeoutException>(() => service.GetExchangeRatesAsync());
    }

    [Fact]
    public async Task Service_ShouldClassifyHttpFailureAsInfrastructure()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        await Assert.ThrowsAsync<InfrastructureServiceException>(() => service.GetExchangeRatesAsync());
    }

    [Fact]
    public async Task Service_ShouldClassifyUnexpectedHtmlAsInfrastructureProtocolFailure()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body><p>site redesigned</p></body></html>")
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var error = await Assert.ThrowsAsync<InfrastructureServiceException>(() =>
            service.GetExchangeRatesAsync());

        Assert.Contains("页面结构", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_ShouldRejectRowsWithoutNumericRates()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<table><tr><td>美元</td><td>-</td><td>-</td><td>-</td><td>-</td><td>-</td></tr></table>")
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            new StubSettingsService("https://rates.example/start"),
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        await Assert.ThrowsAsync<InfrastructureServiceException>(() => service.GetExchangeRatesAsync());
    }

    [Fact]
    public async Task Service_ShouldReturnAnEmptyListWithoutNetworkAccessWhenNoCurrencyIsConfigured()
    {
        var handler = new SequenceHandler(_ => throw new InvalidOperationException("HTTP should not be reached."));
        var settings = new StubSettingsService("https://rates.example/start");
        settings.Settings.ExchangeRate.SelectedCurrencies = [];
        settings.Settings.ExchangeRate.AllSupportedCurrencies = [];
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var service = new BocExchangeRateService(
            settings,
            client,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        var rates = await service.GetExchangeRatesAsync();

        Assert.Empty(rates);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public StubSettingsService(string url)
        {
            Settings.ExchangeRate.Url = url;
            Settings.ExchangeRate.SelectedCurrencies = ["美元"];
        }

        public AppSettings Settings { get; } = new();
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Interlocked.Increment(ref _requestCount) - 1;
            RequestUris.Add(request.RequestUri!);
            if (index >= responses.Length)
            {
                throw new InvalidOperationException("No response was configured for this request.");
            }

            return Task.FromResult(responses[index](request));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}

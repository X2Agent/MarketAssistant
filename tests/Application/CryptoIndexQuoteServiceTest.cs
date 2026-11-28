using System.Net;
using System.Text;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class CryptoIndexQuoteServiceTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("coingecko.test", "恐惧贪婪", 1, DisplayName = "CoinGecko fails")]
    [DataRow("alternative.test", "总市值", 2, DisplayName = "AlternativeMe fails")]
    public async Task GetLatestAsync_OneSourceFails_ShouldKeepOtherSource(
        string failedHost, string expectedFirstName, int expectedCount)
    {
        var service = CreateService(request =>
            request.RequestUri!.Host == failedHost
                ? throw new HttpRequestException("source down")
                : SuccessResponse(request.RequestUri!.Host));

        var items = await service.GetLatestAsync();

        Assert.HasCount(expectedCount, items);
        Assert.AreEqual(expectedFirstName, items[0].Name);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetLatestAsync_AllSourcesFail_ShouldThrowWithBothFailures()
    {
        var service = CreateService(_ => throw new HttpRequestException("source down"));

        var exception = await Assert.ThrowsExactlyAsync<FriendlyException>(() => service.GetLatestAsync());

        var aggregate = exception.InnerException as AggregateException;
        Assert.IsNotNull(aggregate);
        Assert.HasCount(2, aggregate!.InnerExceptions);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetLatestAsync_BothSourcesReturnNoData_ShouldReturnEmpty()
    {
        var service = CreateService(request => request.RequestUri!.Host == "coingecko.test"
            ? Json("{}")
            : Json("{\"data\":[]}"));

        var items = await service.GetLatestAsync();

        Assert.IsEmpty(items);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetLatestAsync_Cancelled_ShouldPropagateCancellation()
    {
        var service = CreateService(request => SuccessResponse(request.RequestUri!.Host));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetLatestAsync(cancellation.Token));
    }

    private static CryptoIndexQuoteService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns((string name) => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri(name == "CoinGecko" ? "https://coingecko.test/" : "https://alternative.test/")
            });
        return new CryptoIndexQuoteService(
            new CoinGeckoApiService(factory.Object, NullLogger<CoinGeckoApiService>.Instance),
            new AlternativeMeClient(factory.Object));
    }

    private static HttpResponseMessage SuccessResponse(string host) => host == "coingecko.test"
        ? Json("""
            {"data":{"total_market_cap":{"usd":2500000000000},"market_cap_change_percentage_24h_usd":1.25,"market_cap_percentage":{"btc":55.5}}}
            """)
        : Json("{\"data\":[{\"value\":\"42\",\"value_classification\":\"Fear\"}]}");

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

using System.Net;
using System.Text;
using MarketAssistant.Applications.Settings;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

[TestClass]
public sealed class CryptoPortfolioServiceSpotTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotPositions_NetworkFailure_ShouldPropagate()
    {
        var service = CreateService(
            _ => throw new HttpRequestException("network down"),
            CreateSpotClient(new ExchangeBalance { Asset = "BTC", Free = 1m }));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => service.GetCurrentPositionsAsync());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotPositions_InvalidSymbol_ShouldSkipUnsupportedAsset()
    {
        var service = CreateService(
            _ => Json("{\"code\":-1121,\"msg\":\"Invalid symbol.\"}", HttpStatusCode.BadRequest),
            CreateSpotClient(new ExchangeBalance { Asset = "UNSUPPORTED", Free = 1m }));

        var positions = await service.GetCurrentPositionsAsync();

        Assert.IsEmpty(positions);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BalanceSummary_NetworkFailure_ShouldPropagate()
    {
        var service = CreateService(
            _ => throw new HttpRequestException("network down"),
            CreateSpotClient(new ExchangeBalance { Asset = "BTC", Free = 1m }));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => service.GetAccountBalanceSummaryAsync(useCache: false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BalanceSummary_InvalidSymbolInBatch_ShouldValueSupportedAssets()
    {
        var service = CreateService(
            request =>
            {
                var query = request.RequestUri!.Query;
                if (query.Contains("symbols=", StringComparison.Ordinal))
                    return Json("{\"code\":-1121,\"msg\":\"Invalid symbol.\"}", HttpStatusCode.BadRequest);
                if (query.Contains("BTCUSDT", StringComparison.Ordinal))
                    return Json("{\"symbol\":\"BTCUSDT\",\"lastPrice\":\"60000\"}");
                return Json("{\"code\":-1121,\"msg\":\"Invalid symbol.\"}", HttpStatusCode.BadRequest);
            },
            CreateSpotClient(
                new ExchangeBalance { Asset = "BTC", Free = 1m },
                new ExchangeBalance { Asset = "UNSUPPORTED", Free = 2m }));

        var summary = await service.GetAccountBalanceSummaryAsync(useCache: false);

        Assert.AreEqual(60000m, summary.TotalValueUSDT);
        Assert.AreEqual(60000m, summary.Assets.Single(asset => asset.Asset == "BTC").ValueUSDT);
        Assert.AreEqual(0m, summary.Assets.Single(asset => asset.Asset == "UNSUPPORTED").ValueUSDT);
    }

    private static IExchangeClient CreateSpotClient(params ExchangeBalance[] balances)
    {
        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(false);
        client.Setup(c => c.GetAccountInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountInfo { Balances = [.. balances] });
        return client.Object;
    }

    private static CryptoPortfolioService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IExchangeClient exchangeClient)
    {
        var handler = new StubHandler(responder);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.binance.com/")
            });
        var marketData = new BinanceMarketDataService(
            factory.Object,
            NullLogger<BinanceMarketDataService>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CryptoTradingMode = TradingMode.LiveSpot });
        var environment = new TradingEnvironmentService(
            settingService.Object,
            Mock.Of<IMarketMonitorProvider>(),
            NullLogger<TradingEnvironmentService>.Instance);
        var dataService = new Mock<TradingDataService>(environment, NullLogger<TradingDataService>.Instance);
        dataService.Setup(s => s.GetOpenPositionAvgEntryPriceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        return new CryptoPortfolioService(
            exchangeClient,
            marketData,
            dataService.Object,
            environment,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CryptoPortfolioService>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

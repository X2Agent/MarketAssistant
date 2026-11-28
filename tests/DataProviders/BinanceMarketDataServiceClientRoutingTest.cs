using System.Net;
using System.Text;
using MarketAssistant.DataProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// BinanceMarketDataService 命名客户端路由：现货公共行情必须走只读行情主机客户端（BinanceMarketData，
/// 不受 451 地区限制），期货衍生品数据走 BinanceFutures。二者错配会让受限出口 IP 下的用户丢失全部现货行情。
/// 桩 HttpMessageHandler，不访问真实网络。
/// </summary>
[TestClass]
[TestCategory("Unit")]
public sealed class BinanceMarketDataServiceClientRoutingTest
{
    private const string SpotClientName = "BinanceMarketData";
    private const string FuturesClientName = "BinanceFutures";

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            // 对象型端点返回 {}，exchangeInfo 返回带 symbols 的对象，其余端点返回数组
            var json = path.Contains("exchangeInfo", StringComparison.Ordinal) ? """{"symbols":[]}"""
                : path.Contains("ticker/24hr?symbol=", StringComparison.Ordinal)
                    || path.Contains("/depth", StringComparison.Ordinal)
                    || path.Contains("premiumIndex", StringComparison.Ordinal) ? "{}"
                : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static (BinanceMarketDataService Service, List<string> ClientNames) CreateService()
    {
        var clientNames = new List<string>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns((string name) =>
            {
                clientNames.Add(name);
                return new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://example.test") };
            });
        var service = new BinanceMarketDataService(
            factory.Object,
            NullLogger<BinanceMarketDataService>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
        return (service, clientNames);
    }

    [TestMethod]
    public async Task SpotMarketData_UsesReadOnlyMarketDataClient()
    {
        var (service, clientNames) = CreateService();

        await service.Get24hrTickerAsync("BTCUSDT");
        await service.Get24hrTickersAsync(["BTCUSDT", "ETHUSDT"]);
        await service.GetAll24hrTickersAsync();
        await service.GetAll24hrTickersFullAsync();
        await service.GetExchangeInfoAsync();
        await service.GetKlinesAsync("BTCUSDT");
        await service.GetDepthAsync("BTCUSDT");
        await service.GetRecentTradesAsync("BTCUSDT");

        Assert.HasCount(8, clientNames);
        Assert.IsTrue(
            clientNames.All(name => name == SpotClientName),
            "现货公共行情必须全部走 BinanceMarketData，实际: " + string.Join(", ", clientNames));
    }

    [TestMethod]
    public async Task FuturesDerivatives_KeepUsingFuturesClient()
    {
        var (service, clientNames) = CreateService();

        await service.GetPremiumIndexAsync("BTCUSDT");
        await service.GetFundingRateHistoryAsync("BTCUSDT");
        await service.GetLongShortRatioAsync(LongShortRatioEndpoint.GlobalLongShortAccountRatio, "BTCUSDT");
        await service.GetOpenInterestHistAsync("BTCUSDT");

        Assert.HasCount(4, clientNames);
        Assert.IsTrue(
            clientNames.All(name => name == FuturesClientName),
            "期货衍生品数据必须走 BinanceFutures，实际: " + string.Join(", ", clientNames));
    }
}

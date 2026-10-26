using System.Net;
using System.Text;
using MarketAssistant.Applications.Market;
using MarketAssistant.DataProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// CryptoOrderBookService / CryptoTickTradeService 消息层单元测试：
/// 覆盖币安返回体的解析、排序契约，以及空/畸形响应（桩 HttpMessageHandler，不访问真实网络）。
/// </summary>
[TestClass]
public class CryptoMarketSectionServiceTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static BinanceMarketDataService CreateBinanceService(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        // 被测代码 using 释放 HttpClient，故每次调用返回共享 handler 的新实例
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.binance.com/")
            });
        return new BinanceMarketDataService(
            factory.Object,
            NullLogger<BinanceMarketDataService>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static CryptoOrderBookService CreateOrderBookService(StubHandler handler) =>
        new(CreateBinanceService(handler), NullLogger<CryptoOrderBookService>.Instance);

    private static CryptoTickTradeService CreateTickTradeService(StubHandler handler) =>
        new(CreateBinanceService(handler), NullLogger<CryptoTickTradeService>.Instance);

    #region 盘口深度

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetOrderBookAsync_ShouldParseStringNumbersAndKeepSourceOrder()
    {
        // 币安以字符串返回数值；买盘降序、卖盘升序由服务端保证
        const string payload = """
            {
              "lastUpdateId": 9876543210,
              "bids": [["68000.10", "1.5"], ["67999.00", "2"]],
              "asks": [["68001.00", "0.75"], ["68002.50", "3"]]
            }
            """;
        var handler = new StubHandler(_ => Json(payload));
        var service = CreateOrderBookService(handler);

        var snapshot = await service.GetOrderBookAsync("BTCUSDT", depth: 2);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(9876543210L, snapshot.LastUpdateId);
        Assert.HasCount(2, snapshot.Bids);
        Assert.HasCount(2, snapshot.Asks);
        Assert.AreEqual(68000.10m, snapshot.Bids[0].Price);
        Assert.AreEqual(1.5m, snapshot.Bids[0].Quantity);
        Assert.AreEqual(68002.50m, snapshot.Asks[1].Price);
        Assert.AreEqual(68000.10m, snapshot.BestBid);
        Assert.AreEqual(68001.00m, snapshot.BestAsk);
        Assert.AreEqual(0.90m, snapshot.Spread);
        Assert.AreEqual(3.5m, snapshot.TotalBidQuantity);
        Assert.AreEqual(3.75m, snapshot.TotalAskQuantity);
        Assert.Contains("symbol=BTCUSDT", handler.RequestUrls.Single());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetOrderBookAsync_ShouldNormalizeDepthToBinanceAllowedLimit()
    {
        var handler = new StubHandler(_ => Json("""{"bids":[["1","1"]],"asks":[]}"""));
        var service = CreateOrderBookService(handler);

        await service.GetOrderBookAsync("BTCUSDT", depth: 7);

        // 币安只接受 5/10/20/...，7 档向上吸附到 10
        Assert.Contains("limit=10", handler.RequestUrls.Single());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetOrderBookAsync_WithBothSidesEmpty_ShouldReturnNull()
    {
        var handler = new StubHandler(_ => Json("""{"lastUpdateId":1,"bids":[],"asks":[]}"""));
        var service = CreateOrderBookService(handler);

        Assert.IsNull(await service.GetOrderBookAsync("BTCUSDT"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetOrderBookAsync_WithMalformedLevels_ShouldSkipUnparsableEntries()
    {
        // 单档长度不足、数值非法、非数组条目都应被跳过而不是抛异常
        const string payload = """
            {
              "lastUpdateId": "not-a-number",
              "bids": [["68000.00","1"], ["68001.00"], ["oops","2"], "junk"],
              "asks": [["68002.00","1"]]
            }
            """;
        var handler = new StubHandler(_ => Json(payload));
        var service = CreateOrderBookService(handler);

        var snapshot = await service.GetOrderBookAsync("BTCUSDT");

        Assert.IsNotNull(snapshot);
        Assert.HasCount(2, snapshot.Bids, "长度不足与非法价格的档位处理方式不同");
        Assert.AreEqual(68000.00m, snapshot.Bids[0].Price);
        Assert.AreEqual(0m, snapshot.Bids[1].Price, "无法解析的价格应回退为 0");
        Assert.HasCount(1, snapshot.Asks);
        Assert.AreEqual(0L, snapshot.LastUpdateId, "无法解析的序号应回退为 0");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetOrderBookAsync_WithBlankCode_ShouldNotIssueRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("不应发起 HTTP 请求"));
        var service = CreateOrderBookService(handler);

        Assert.IsNull(await service.GetOrderBookAsync("  "));
        Assert.IsEmpty(handler.RequestUrls);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NormalizeLimit_ShouldSnapUpToTheNearestAllowedValue()
    {
        Assert.AreEqual(5, CryptoOrderBookService.NormalizeLimit(0));
        Assert.AreEqual(5, CryptoOrderBookService.NormalizeLimit(5));
        Assert.AreEqual(10, CryptoOrderBookService.NormalizeLimit(6));
        Assert.AreEqual(100, CryptoOrderBookService.NormalizeLimit(100));
        Assert.AreEqual(5000, CryptoOrderBookService.NormalizeLimit(6000));
    }

    #endregion

    #region 逐笔成交

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetRecentTradesAsync_ShouldParseFieldsAndReturnNewestFirst()
    {
        // 币安按时间升序返回，对外契约要求最新在前
        const string payload = """
            [
              {"id":1,"price":"68000.00","qty":"0.5","time":1700000000000,"isBuyerMaker":true},
              {"id":2,"price":"68010.00","qty":"0.25","time":1700000060000,"isBuyerMaker":false}
            ]
            """;
        var handler = new StubHandler(_ => Json(payload));
        var service = CreateTickTradeService(handler);

        var trades = await service.GetRecentTradesAsync("BTCUSDT", limit: 2);

        Assert.HasCount(2, trades);
        Assert.AreEqual(2L, trades[0].TradeId, "最新一笔应排在最前");
        Assert.AreEqual(68010.00m, trades[0].Price);
        Assert.AreEqual(0.25m, trades[0].Quantity);
        Assert.IsFalse(trades[0].IsBuyerMaker);
        Assert.IsTrue(trades[0].IsActiveBuy);
        Assert.AreEqual(1L, trades[1].TradeId);
        Assert.IsTrue(trades[1].IsBuyerMaker);
        Assert.IsFalse(trades[1].IsActiveBuy);
        Assert.AreEqual(1700000060000L, new DateTimeOffset(trades[0].Timestamp).ToUnixTimeMilliseconds());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetRecentTradesAsync_WithEmptyArray_ShouldReturnEmptyList()
    {
        var handler = new StubHandler(_ => Json("[]"));
        var service = CreateTickTradeService(handler);

        Assert.IsEmpty(await service.GetRecentTradesAsync("BTCUSDT"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetRecentTradesAsync_WithMalformedEntries_ShouldSkipThemWithoutThrowing()
    {
        const string payload = """
            [
              {"price":"68000.00","qty":"0.5","time":"not-a-number","isBuyerMaker":"yes"},
              "junk",
              {"id":7,"price":"bad","qty":"0.1","time":1700000060000,"isBuyerMaker":false}
            ]
            """;
        var handler = new StubHandler(_ => Json(payload));
        var service = CreateTickTradeService(handler);

        var trades = await service.GetRecentTradesAsync("BTCUSDT");

        Assert.HasCount(2, trades, "非对象条目应被跳过，其余条目应保留");
        Assert.AreEqual(7L, trades[0].TradeId, "最新的合法条目应排在最前");
        Assert.AreEqual(0m, trades[0].Price, "无法解析的价格应回退为 0");
        Assert.AreEqual(68000.00m, trades[1].Price);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetRecentTradesAsync_WithBlankCode_ShouldNotIssueRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("不应发起 HTTP 请求"));
        var service = CreateTickTradeService(handler);

        Assert.IsEmpty(await service.GetRecentTradesAsync("  "));
        Assert.IsEmpty(handler.RequestUrls);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetRecentTradesAsync_ShouldClampLimitToBinanceRange()
    {
        var handler = new StubHandler(_ => Json("[]"));
        var service = CreateTickTradeService(handler);

        await service.GetRecentTradesAsync("BTCUSDT", limit: 5000);

        Assert.Contains("limit=1000", handler.RequestUrls.Single());
    }

    #endregion
}

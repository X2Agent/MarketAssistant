using System.Net;
using System.Net.WebSockets;
using MarketAssistant.DataProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// Binance WebSocket 地区限制判定：握手 451 必须被识别以停止无意义的无限重连，
/// 同时不能把消息中恰好包含 "451" 的其他异常（端口号、字节数等）误判为地区限制。
/// </summary>
[TestClass]
public sealed class BinanceWebSocketServiceTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void IsRegionUnavailable_ShouldMatchStatusCode451()
    {
        // .NET 框架握手失败时写入消息的固定格式（实测）
        var ex = new WebSocketException("The server returned status code '451'.");

        Assert.IsTrue(BinanceWebSocketService.IsRegionUnavailable(ex));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsRegionUnavailable_ShouldNotMatchOtherStatusCodes()
    {
        var ex = new WebSocketException("The server returned status code '1006'.");

        Assert.IsFalse(BinanceWebSocketService.IsRegionUnavailable(ex));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsRegionUnavailable_ShouldNotMatchIncidental451InMessage()
    {
        var ex = new WebSocketException("Unable to connect to the remote server: port 45153 in use");

        Assert.IsFalse(BinanceWebSocketService.IsRegionUnavailable(ex));
    }
}

/// <summary>
/// Binance REST 地区限制建型：451 状态码与"200 + 受限文案"都必须抛出
/// <see cref="BinanceRegionUnavailableException"/>（只判其一会漏掉另一种实测形态）。
/// </summary>
[TestClass]
public sealed class BinanceMarketDataServiceRegionBlockTest
{
    private sealed class StubHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(content) });
    }

    private sealed class StubHttpClientFactory(HttpStatusCode statusCode, string content) : IHttpClientFactory
    {
        // 生产环境的命名客户端带 BaseAddress，服务内使用相对路径，桩须保持一致
        public HttpClient CreateClient(string name)
            => new(new StubHandler(statusCode, content)) { BaseAddress = new Uri("https://api.binance.com") };
    }

    private static BinanceMarketDataService CreateService(HttpStatusCode statusCode, string content)
        => new(
            new StubHttpClientFactory(statusCode, content),
            NullLogger<BinanceMarketDataService>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Get24hrTickerAsync_WithStatusCode451_ShouldThrowRegionUnavailable()
    {
        var service = CreateService(HttpStatusCode.UnavailableForLegalReasons, "{}");

        var ex = await Assert.ThrowsAsync<BinanceRegionUnavailableException>(
            () => service.Get24hrTickerAsync("BTCUSDT"));

        StringAssert.Contains(ex.Message, "地区不可用", "异常消息须为用户可读的地区限制指引");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Get24hrTickerAsync_WithOkAndRestrictedBody_ShouldThrowRegionUnavailable()
    {
        var service = CreateService(
            HttpStatusCode.OK,
            "{\"code\":0,\"msg\":\"Restricted location, please check your region\"}");

        await Assert.ThrowsAsync<BinanceRegionUnavailableException>(
            () => service.Get24hrTickerAsync("BTCUSDT"));
    }
}

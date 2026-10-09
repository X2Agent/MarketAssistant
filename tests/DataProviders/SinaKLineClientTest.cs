using System.Net;
using MarketAssistant.DataProviders.AShare;
using Moq;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// <see cref="SinaKLineClient"/> 单元测试（Mock HttpMessageHandler，不触网）。
/// 覆盖实测边界：null 响应、datalen 钳制、分钟线含 amount 而日线不含。
/// </summary>
[TestClass]
public sealed class SinaKLineClientTest
{
    private const string DailyBarsJson = """
    [
        {"day":"2026-10-06","open":"140.683","high":"140.718","low":"140.668","close":"140.715","volume":"43112800"},
        {"day":"2026-10-08","open":"140.735","high":"140.766","low":"140.696","close":"140.705","volume":"19345000"}
    ]
    """;

    private const string MinuteBarsJson = """
    [
        {"day":"2026-10-08 14:45:00","open":"140.706","high":"140.715","low":"140.704","close":"140.709","volume":"175200","amount":"24652096.4092"},
        {"day":"2026-10-08 15:00:00","open":"140.710","high":"140.715","low":"140.704","close":"140.705","volume":"167000","amount":"23498214.8203"}
    ]
    """;

    private static SinaKLineClient CreateClient(
        string responseContent,
        Action<HttpRequestMessage>? requestInspector = null)
    {
        var handler = new StubHandler(request =>
        {
            requestInspector?.Invoke(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent),
            };
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler) { BaseAddress = new Uri("https://quotes.sina.cn/") });

        return new SinaKLineClient(factoryMock.Object);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineBarsAsync_DailyBars_ShouldParseWithoutAmount()
    {
        var client = CreateClient(DailyBarsJson);

        var bars = await client.GetKLineBarsAsync("sh511010", scale: 240, count: 2);

        Assert.HasCount(2, bars);
        Assert.AreEqual("2026-10-08", bars[1].Day);
        Assert.AreEqual(140.735m, bars[1].Open);
        Assert.AreEqual(140.766m, bars[1].High);
        Assert.AreEqual(140.696m, bars[1].Low);
        Assert.AreEqual(140.705m, bars[1].Close);
        Assert.AreEqual(19345000m, bars[1].Volume);
        Assert.IsNull(bars[1].Amount, "日/周/月响应不含成交额，必须为 null 而非 0");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineBarsAsync_MinuteBars_ShouldParseAmount()
    {
        var client = CreateClient(MinuteBarsJson);

        var bars = await client.GetKLineBarsAsync("sh511010", scale: 15, count: 2);

        Assert.HasCount(2, bars);
        Assert.AreEqual("2026-10-08 15:00:00", bars[1].Day);
        Assert.AreEqual(140.705m, bars[1].Close);
        Assert.AreEqual(23498214.8203m, bars[1].Amount, "分钟线响应含成交额");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineBarsAsync_NullResponse_ShouldReturnEmptyList()
    {
        // 无效代码 / 非法 scale / datalen 超限时接口返回 JSON null（非数组）
        var client = CreateClient("null");

        var bars = await client.GetKLineBarsAsync("sh999999", scale: 240, count: 10);

        Assert.IsEmpty(bars);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineBarsAsync_ShouldClampDataLengthAndPassScale()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(DailyBarsJson, request => captured = request);

        await client.GetKLineBarsAsync("sh511010", scale: 1200, count: 0);

        Assert.IsNotNull(captured);
        var query = captured!.RequestUri!.Query;
        Assert.Contains("symbol=sh511010", query);
        Assert.Contains("scale=1200", query);
        Assert.Contains("ma=no", query);
        Assert.Contains("datalen=1", query, "datalen=0 会返回全历史，必须钳制到 1");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineBarsAsync_CountAboveLimit_ShouldClampToMax()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(DailyBarsJson, request => captured = request);

        await client.GetKLineBarsAsync("sh511010", scale: 240, count: 5000);

        Assert.IsNotNull(captured);
        Assert.Contains($"datalen={SinaKLineClient.MaxBarCount}", captured!.RequestUri!.Query,
            "超过上限时间接口返回空，必须钳制到 MaxBarCount");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineBarsAsync_EmptySymbol_ShouldReturnEmptyListWithoutRequest()
    {
        var handler = new ThrowingStubHandler();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler));

        var client = new SinaKLineClient(factoryMock.Object);

        Assert.IsEmpty(await client.GetKLineBarsAsync(string.Empty, 240, 10));
        Assert.IsEmpty(await client.GetKLineBarsAsync("   ", 240, 10));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class ThrowingStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("不应发起请求");
    }
}

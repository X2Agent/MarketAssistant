using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using Moq;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// P1-05：A 股客户端反序列化容错——字符串数字、null、"--" 占位均不抛异常。
/// </summary>
[TestClass]
public class AShareClientDeserializationTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private static IHttpClientFactory CreateFactory(StubHandler handler)
    {
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://test.local/") };
                return httpClient;
            });
        return httpClientFactoryMock.Object;
    }

    private sealed class SampleRow
    {
        public int T { get; set; }
        public decimal Value { get; set; }
        public decimal? Maybe { get; set; }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetListAsync_StringNumbersNullAndPlaceholder_ShouldDeserializeTolerantly()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "[{\"t\":20240101,\"value\":\"12.34\",\"maybe\":null},{\"t\":20240102,\"value\":56.78,\"maybe\":\"--\"}]"),
        });

        var client = new ZhiTuMarketClient(CreateFactory(handler));
        var rows = await client.GetListAsync<SampleRow>("/fake/path");

        Assert.HasCount(2, rows);
        Assert.AreEqual(20240101, rows[0].T);
        Assert.AreEqual(12.34m, rows[0].Value);
        Assert.IsNull(rows[0].Maybe);
        Assert.AreEqual(20240102, rows[1].T);
        Assert.AreEqual(56.78m, rows[1].Value);
        // "--" 占位应安全降级为 null 而非抛出转换异常
        Assert.IsNull(rows[1].Maybe);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UnwrapJsonp_ShouldStripWrapper()
    {
        var jsonp = "jQuery({\"result\":1});";
        var json = EastMoneyNewsClient.UnwrapJsonp(jsonp);
        Assert.AreEqual("{\"result\":1}", json);

        // 非法格式原样返回
        Assert.AreEqual("not-json", EastMoneyNewsClient.UnwrapJsonp("not-json"));
        Assert.AreEqual(string.Empty, EastMoneyNewsClient.UnwrapJsonp(""));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ClsQuoteClient_DataNull_ShouldReturnNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":null}"),
        });

        var client = new ClsQuoteClient(CreateFactory(handler));
        var quote = await client.GetStockQuoteAsync("sh600519", "last_px");

        Assert.IsNull(quote);
    }
}
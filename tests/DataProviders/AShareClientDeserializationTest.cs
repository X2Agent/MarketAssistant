using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Applications.Market;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text.Json;

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
    public async Task ClsQuoteClient_NullPriceAndChange_ShouldDeserializeWithoutThrowing()
    {
        // 复现线上崩溃堆栈：AShareAssetInfoService 请求 last_px/change，
        // CLS 对停牌/无成交股票返回 null，非空 decimal 解析曾直接抛 JsonException
        var handler = new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":{\"secu_name\":\"测试股票\",\"secu_code\":\"SH600000\",\"last_px\":null,\"change\":null}}"),
        });

        var client = new ClsQuoteClient(CreateFactory(handler));
        var quote = await client.GetStockQuoteAsync("sh600000", "secu_name,secu_code,last_px,change");

        Assert.IsNotNull(quote);
        Assert.AreEqual("测试股票", quote.SecurityName);
        Assert.IsNull(quote.LastPrice, "停牌/无成交时应为 null（未提供），不得伪造 0");
        Assert.IsNull(quote.Change);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ClsQuoteClient_NullAndPlaceholderFields_ShouldMapToNull()
    {
        // pe 亏损/不适用为 null；up_price 无涨跌停价为 "--" 占位；amp 停牌为 null；ttm_pe 空串
        var handler = new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":{\"last_px\":\"12.34\",\"change\":-0.82,\"pe\":null,\"up_price\":\"--\",\"amp\":null,\"ttm_pe\":\"\"}}"),
        });

        var client = new ClsQuoteClient(CreateFactory(handler));
        var quote = await client.GetStockQuoteAsync("sh600000", "last_px,change,pe,up_price,amp,ttm_pe");

        Assert.IsNotNull(quote);
        Assert.AreEqual(12.34m, quote.LastPrice);
        Assert.IsNull(quote.PERatio);
        Assert.IsNull(quote.UpPrice);
        Assert.IsNull(quote.Amplitude);
        Assert.IsNull(quote.TTMPERatio);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NonNullableDecimal_WhenJsonNull_ShouldStillThrow()
    {
        // 不变量：非空 decimal 字段收到 null 必须抛错（StringToDecimalConverter 的上游契约违约哨兵），
        // 防止未来回退到"静默归零"——0 会被下游当成合法行情参与计算
        var exception = Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize<List<SampleRow>>("[{\"t\":1,\"value\":null}]", AShareJsonOptions.Instance));

        StringAssert.Contains(exception.Message, "无法将");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AShareMarketSnapshotService_NullAndPlaceholderFields_ShouldMapToNull()
    {
        // 财联社缺失字段可能为 null、"--" 或 0，行情速览统一归一为 null（未提供）
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":{\"last_px\":null,\"change\":null,\"open_px\":10.5,\"high_px\":11.0,\"pe\":null,\"pb\":\"--\"}}"),
        });

        var service = new AShareMarketSnapshotService(
            new ClsQuoteClient(CreateFactory(handler)),
            new SinaStockQuoteClient(CreateFactory(new StubHandler(
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)))),
            Mock.Of<ILogger<AShareMarketSnapshotService>>());

        var snapshot = await service.GetSnapshotAsync("sh600000");

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(10.5m, snapshot.Open);
        Assert.AreEqual(11.0m, snapshot.High);
        Assert.IsNull(snapshot.Pe);
        Assert.IsNull(snapshot.Pb);
        Assert.IsTrue(snapshot.HasAnyValue);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AShareMarketSnapshotService_ClsAllNull_ShouldFallBackToSina()
    {
        // 复现线上问题：财联社对基金/ETF 返回全 null，需回退新浪个股行情补齐开高低收与成交量额
        var handler = new StubHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.AbsolutePath.Contains("list=", StringComparison.Ordinal)
                        ? "var hq_str_sh511010=\"国债ETF国泰,140.735,140.735,140.705,140.766,140.696,140.705,140.706,19345000,2721943053.000,43000,140.705,73000,140.704,111400,140.703,2400,140.698,20100,140.697,3300,140.706,200,140.708,1500,140.709,2700,140.710,20200,140.711,2026-10-08,15:34:59,00,D|400|56282.00\";"
                        : "{\"data\":{\"secu_name\":null,\"secu_code\":null,\"last_px\":null,\"change\":null,\"open_px\":null,\"high_px\":null,\"low_px\":null,\"preclose_px\":null,\"business_amount\":null,\"business_balance\":null,\"tr\":null,\"amp\":null,\"qrr\":null,\"pe\":null,\"pb\":null}}"),
            });

        var service = new AShareMarketSnapshotService(
            new ClsQuoteClient(CreateFactory(handler)),
            new SinaStockQuoteClient(CreateFactory(handler)),
            Mock.Of<ILogger<AShareMarketSnapshotService>>());

        var snapshot = await service.GetSnapshotAsync("sh511010");

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(140.735m, snapshot.Open);
        Assert.AreEqual(140.735m, snapshot.PreClose);
        Assert.AreEqual(140.766m, snapshot.High);
        Assert.AreEqual(140.696m, snapshot.Low);
        Assert.AreEqual(19345000m, snapshot.Volume);
        Assert.AreEqual(2721943053m, snapshot.Amount);
        // 振幅 = (最高-最低)/昨收×100% = 0.07/140.735*100 ≈ 0.05
        Assert.AreEqual(0.05m, snapshot.Amplitude);
        // 新浪不提供的指标保持 null，由 UI 隐藏而非伪造 0
        Assert.IsNull(snapshot.TurnoverRate);
        Assert.IsNull(snapshot.VolumeRatio);
        Assert.IsNull(snapshot.Pe);
        Assert.IsNull(snapshot.Pb);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AShareMarketSnapshotService_BothSourcesEmpty_ShouldReturnNull()
    {
        var handler = new StubHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.AbsolutePath.Contains("list=", StringComparison.Ordinal)
                        ? "var hq_str_sh999999=\"\";"
                        : "{\"data\":null}"),
            });

        var service = new AShareMarketSnapshotService(
            new ClsQuoteClient(CreateFactory(handler)),
            new SinaStockQuoteClient(CreateFactory(handler)),
            Mock.Of<ILogger<AShareMarketSnapshotService>>());

        Assert.IsNull(await service.GetSnapshotAsync("sh999999"));
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
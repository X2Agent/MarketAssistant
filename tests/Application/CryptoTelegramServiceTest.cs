using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Telegrams;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// CryptoTelegramService（Odaily 快讯，唯一来源）消息层单元测试：字段映射、重要快讯过滤、
/// HTML 清洗，以及「失败必须抛出、空列表只表示确实没有快讯」的契约（桩 HttpMessageHandler，不访问真实网络）。
/// 夹具字段形态取自 Odaily 真实响应；错误以 HTTP 200 + success=false 承载。
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class CryptoTelegramServiceTest
{
    private const long PublishTimestamp = 1_700_000_000_000;

    private const string FlashListJson = """
        {"code":0,"msg":"success","success":true,"serverTimestamp":1700000200000,
         "data":{"total":2,"pageSize":100,"pageNum":1,"list":[
           {"id":1,"title":"比特币突破 10 万美元",
            "content":"<p>Odaily星球日报讯 据 CoinDesk 报道，BTC 突破 10 万美元。</p><p>&nbsp;</p><p>第二段内容</p>",
            "images":[],"isImportant":true,"publishTimestamp":1700000000000,
            "sourceUrl":"https://example.test/source","link":"https://www.odaily.news/zh-CN/newsflash/1"},
           {"id":2,"title":"普通快讯","content":"<p>普通内容</p>","images":[],"isImportant":false,
            "publishTimestamp":1700000100000,"link":"https://www.odaily.news/zh-CN/newsflash/2"}
         ]}}
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> RequestUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static CryptoTelegramService CreateService(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("CryptoTelegram"))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.odaily.news/")
            });

        var aliasRegistry = new Mock<ICryptoAliasRegistry>();
        aliasRegistry.Setup(r => r.GetMatchPatternsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Regex>
            {
                ["BTC"] = new Regex("BTC|比特币"),
                ["ETH"] = new Regex("ETH|以太坊")
            });

        return new CryptoTelegramService(NullLogger<CryptoTelegramService>.Instance, factory.Object, aliasRegistry.Object);
    }

    [TestMethod]
    public async Task GetTelegraphsAsync_KeepsOnlyImportantFlashes_AndMapsFields()
    {
        var handler = new StubHandler(_ => Json(FlashListJson));
        var service = CreateService(handler);

        var telegrams = await service.GetTelegraphsAsync();

        Assert.AreEqual("/api/v1/newsflash?page=1&size=100&lang=zh-cn", handler.RequestUrls.Single());
        var telegram = telegrams.Single();
        Assert.AreEqual("比特币突破 10 万美元", telegram.Title);
        Assert.AreEqual("https://www.odaily.news/zh-CN/newsflash/1", telegram.Url);
        Assert.IsTrue(telegram.IsImportant);
        Assert.AreEqual(
            DateTimeOffset.FromUnixTimeMilliseconds(PublishTimestamp).ToLocalTime().ToString("HH:mm:ss"),
            telegram.Time);
        CollectionAssert.AreEqual(new[] { "BTC" }, telegram.Symbols);
    }

    [TestMethod]
    public async Task GetTelegraphsAsync_StripsHtmlAndFixedPrefixFromContent()
    {
        var service = CreateService(new StubHandler(_ => Json(FlashListJson)));

        var content = (await service.GetTelegraphsAsync()).Single().Content;

        StringAssert.StartsWith(content, "据 CoinDesk 报道");
        StringAssert.Contains(content, "第二段内容");
        Assert.IsFalse(content.Contains("<p>", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("&nbsp;", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("Odaily星球日报讯", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetTelegraphsAsync_NoImportantFlashes_ReturnsEmptyList()
    {
        const string json = """
            {"code":0,"success":true,"data":{"list":[
              {"id":2,"title":"普通快讯","content":"<p>普通内容</p>","isImportant":false,"publishTimestamp":1700000100000,"link":"x"}
            ]}}
            """;
        var service = CreateService(new StubHandler(_ => Json(json)));

        var telegrams = await service.GetTelegraphsAsync();

        Assert.IsEmpty(telegrams);
    }

    [TestMethod]
    public async Task GetTelegraphsAsync_BusinessErrorWithHttp200_Throws()
    {
        // 实测：服务端故障以 HTTP 200 + success=false 返回；若当作空列表，用户只会看到「暂无快讯」
        const string json = """{"code":500,"msg":"服务器内部错误","data":null,"success":false}""";
        var service = CreateService(new StubHandler(_ => Json(json)));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GetTelegraphsAsync());

        StringAssert.Contains(ex.Message, "code=500");
    }

    [TestMethod]
    public async Task GetTelegraphsAsync_HttpErrorStatus_Throws()
    {
        var service = CreateService(new StubHandler(_ => Json("{}", HttpStatusCode.BadGateway)));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.GetTelegraphsAsync());
    }
}

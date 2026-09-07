using System.Net;
using System.Text;
using MarketAssistant.DataProviders.Web3;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// GoPlusSecurityClient 消息层单元测试（桩 HttpMessageHandler，不访问真实网络）。
/// </summary>
[TestClass]
public class GoPlusSecurityClientTest
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<string> RequestUrls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUrls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(_responder(request));
        }
    }

    private static GoPlusSecurityClient CreateClient(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        // 被测代码 using 释放 HttpClient，故每次调用返回共享 handler 的新实例
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.gopluslabs.io/")
            });
        return new GoPlusSecurityClient(factory.Object, NullLogger<GoPlusSecurityClient>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string BonkMint = "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263";

    [TestMethod]
    public async Task GetTokenSecurityAsync_EvmChainAlias_RoutesToNumericChainId()
    {
        var handler = new StubHandler(_ => Json("""{"code":1,"result":{}}"""));
        var client = CreateClient(handler);

        await client.GetTokenSecurityAsync("eth", "0xabc");
        await client.GetTokenSecurityAsync("1", "0xabc");

        Assert.IsTrue(handler.RequestUrls.All(u =>
            u.StartsWith("/api/v1/token_security/1?contract_addresses=0xabc", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_Solana_RoutesToDedicatedEndpointKeepingCase()
    {
        var json = "{\"code\":1,\"result\":{\"" + BonkMint + "\":{\"token_symbol\":\"BONK\"}}}";
        var handler = new StubHandler(_ => Json(json));
        var client = CreateClient(handler);

        var security = await client.GetTokenSecurityAsync("sol", BonkMint);

        Assert.AreEqual(
            "/api/v1/solana/token_security?contract_addresses=" + BonkMint,
            handler.RequestUrls.Single());
        Assert.AreEqual("BONK", security!.TokenSymbol);
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_LowercasedResultKey_MatchesMixedCaseMint()
    {
        var handler = new StubHandler(_ => Json(
            """{"code":1,"result":{"dezxaz8z7pnrnrjjz3wxborgixca6xjnb7yab1ppb263":{"token_symbol":"BONK"}}}"""));
        var client = CreateClient(handler);

        var security = await client.GetTokenSecurityAsync("solana", BonkMint);

        Assert.AreEqual("BONK", security!.TokenSymbol);
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_NumericLongTailChain_PassesThrough()
    {
        var handler = new StubHandler(_ => Json("""{"code":1,"result":{}}"""));
        var client = CreateClient(handler);

        await client.GetTokenSecurityAsync("324", "0xabc");

        Assert.IsTrue(handler.RequestUrls.Single().StartsWith("/api/v1/token_security/324?", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_UnregisteredNamedChain_FailsFastWithoutHttpCall()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("不应发起 HTTP 请求"));
        var client = CreateClient(handler);

        await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => client.GetTokenSecurityAsync("zksync", "0xabc"));
        Assert.AreEqual(0, handler.RequestUrls.Count);
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_NonSuccessCode_ThrowsFriendly()
    {
        var handler = new StubHandler(_ => Json("""{"code":2022,"message":"Chain not supported","result":null}"""));
        var client = CreateClient(handler);

        await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => client.GetTokenSecurityAsync("eth", "0xabc"));
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_EmptyResult_ReturnsNull()
    {
        var handler = new StubHandler(_ => Json("""{"code":1,"result":{}}"""));
        var client = CreateClient(handler);

        var security = await client.GetTokenSecurityAsync("ethereum", "0xabc");

        Assert.IsNull(security);
    }
}
using System.Net;
using System.Text;
using MarketAssistant.DataProviders.Web3;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// DexScreenerClient 消息层单元测试（桩 HttpMessageHandler，不访问真实网络）。
/// </summary>
[TestClass]
public class DexScreenerClientTest
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

    private static DexScreenerClient CreateClient(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        // 被测代码 using 释放 HttpClient，故每次调用返回共享 handler 的新实例
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.dexscreener.com/")
            });
        return new DexScreenerClient(factory.Object, NullLogger<DexScreenerClient>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task GetTokenPairsAsync_ChainAliases_NormalizeToFullName()
    {
        var handler = new StubHandler(_ => Json("[]"));
        var client = CreateClient(handler);

        await client.GetTokenPairsAsync("1", "0xpepe");
        await client.GetTokenPairsAsync("binance-smart-chain", "0xpepe");
        await client.GetTokenPairsAsync("polygon-pos", "0xpepe");

        CollectionAssert.AreEquivalent(new[]
        {
            "/token-pairs/v1/ethereum/0xpepe",
            "/token-pairs/v1/bsc/0xpepe",
            "/token-pairs/v1/polygon/0xpepe"
        }, handler.RequestUrls);
    }

    [TestMethod]
    public async Task SearchPairsAsync_DeserializesRealResponseShape()
    {
        const string json = """
            {
              "schemaVersion": "1.0.0",
              "pairs": [
                {
                  "chainId": "solana",
                  "dexId": "raydium",
                  "url": "https://dexscreener.com/solana/pair",
                  "baseToken": { "address": "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263", "name": "Bonk", "symbol": "BONK" },
                  "quoteToken": { "address": "So11111111111111111111111111111111111111112", "name": "Wrapped SOL", "symbol": "WSOL" },
                  "priceNative": "1.234e-9",
                  "priceUsd": "0.000003170",
                  "volume": { "h24": 900000.5, "h6": 100000.25, "h1": 10000.125 },
                  "priceChange": { "m5": 0.1, "h1": 1.5, "h6": -2.5, "h24": 5.5 },
                  "liquidity": { "usd": 151502.08, "base": 100, "quote": 50 },
                  "fdv": 281729667,
                  "marketCap": 278977721,
                  "pairCreatedAt": 1677761811000
                }
              ]
            }
            """;
        var handler = new StubHandler(_ => Json(json));
        var client = CreateClient(handler);

        var pairs = await client.SearchPairsAsync("BONK");

        var pair = pairs.Single();
        Assert.AreEqual("solana", pair.ChainId);
        Assert.AreEqual("BONK", pair.BaseToken.Symbol);
        Assert.AreEqual("0.000003170", pair.PriceUsd);
        Assert.AreEqual(900000.5m, pair.Volume!.H24);
        Assert.AreEqual(151502.08m, pair.Liquidity!.Usd);
        Assert.AreEqual(278977721m, pair.MarketCap);
        Assert.AreEqual(1677761811000L, pair.PairCreatedAt);
    }

    [TestMethod]
    public async Task SearchPairsAsync_EmptyPairs_ReturnsEmptyList()
    {
        var handler = new StubHandler(_ => Json("""{"schemaVersion":"1.0.0","pairs":[]}"""));
        var client = CreateClient(handler);

        var pairs = await client.SearchPairsAsync("nonexistent");

        Assert.AreEqual(0, pairs.Count);
    }

    [TestMethod]
    public async Task GetTokenPairsAsync_EmptyTokenAddress_ThrowsFriendly()
    {
        var client = CreateClient(new StubHandler(_ => throw new InvalidOperationException("不应发起 HTTP 请求")));

        await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => client.GetTokenPairsAsync("eth", " "));
    }
}
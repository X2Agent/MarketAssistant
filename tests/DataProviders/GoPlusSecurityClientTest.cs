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
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        public List<string> RequestUrls { get; } = [];

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
    public async Task GetSolanaTokenSecurityAsync_RoutesToDedicatedEndpointKeepingCase()
    {
        var json = "{\"code\":1,\"result\":{\"" + BonkMint + "\":{\"metadata\":{\"symbol\":\"Bonk\"}}}}";
        var handler = new StubHandler(_ => Json(json));
        var client = CreateClient(handler);

        var security = await client.GetSolanaTokenSecurityAsync(BonkMint);

        Assert.AreEqual(
            "/api/v1/solana/token_security?contract_addresses=" + BonkMint,
            handler.RequestUrls.Single());
        Assert.AreEqual("Bonk", security!.Metadata!.Symbol);
    }

    [TestMethod]
    public async Task GetSolanaTokenSecurityAsync_LowercasedResultKey_MatchesMixedCaseMint()
    {
        var handler = new StubHandler(_ => Json(
            """{"code":1,"result":{"dezxaz8z7pnrnrjjz3wxborgixca6xjnb7yab1ppb263":{"metadata":{"symbol":"Bonk"}}}}"""));
        var client = CreateClient(handler);

        var security = await client.GetSolanaTokenSecurityAsync(BonkMint);

        Assert.AreEqual("Bonk", security!.Metadata!.Symbol);
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_SolanaChain_FailsFastWithoutHttpCall()
    {
        // 主端点只接受 EVM 链：误传 Solana 须明确报错，而不是拼出缺链 ID 的 URL
        var handler = new StubHandler(_ => throw new InvalidOperationException("不应发起 HTTP 请求"));
        var client = CreateClient(handler);

        await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => client.GetTokenSecurityAsync("solana", BonkMint));
        Assert.IsEmpty(handler.RequestUrls);
    }

    [TestMethod]
    public async Task GetSolanaTokenSecurityAsync_RealResponseShape_Deserializes()
    {
        // 取自 GoPlus 真实响应（USDC，裁剪了 holders/dex 等无关长数组）：
        // trusted_token 为数字、status/non_transferable 为字符串，transfer_hook 为数组，transfer_fee 为空对象
        const string json = """
            {"code":1,"message":"ok","result":{"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v":{
              "balance_mutable_authority":{"authority":[],"status":"0"},
              "closable":{"authority":[],"status":"0"},
              "creators":[],
              "default_account_state":"1",
              "default_account_state_upgradable":{"authority":[],"status":"0"},
              "dex":[{"burn_percent":null,"day":{"price_max":"-1","price_min":"-1","volume":"78842567.27771530"},"dex_name":"orca","fee_rate":"0.0004"}],
              "freezable":{"authority":[{"address":"7dGbd2QZcCKcTndnHcTL8q7SMVXAkp688NTQYwrRCrar","malicious_address":0}],"status":"1"},
              "holder_count":"9654398",
              "holders":[{"account":"7VHUFJHWu2CuExkJcJrzhQPJ2oygupTWkL2A2For4BmE","balance":"1877005481.661362","is_locked":0,"locked_detail":[],"percent":"0.2115","tag":""}],
              "lp_holders":null,
              "metadata":{"description":"","name":"USD Coin","symbol":"USDC","uri":""},
              "metadata_mutable":{"metadata_upgrade_authority":[{"address":"2wmVCSfPxGPjrnMMn7rchp4uaeoTqN39mXFC2zhPdri9","malicious_address":0}],"status":"1"},
              "mintable":{"authority":[{"address":"BJE5MMbqXjVwjAF7oxwPYXnTXDyspzZyt4vwenNw5ruG","malicious_address":0}],"status":"1"},
              "non_transferable":"0",
              "total_supply":"8873923210.55573",
              "transfer_fee":{},
              "transfer_fee_upgradable":{"authority":[],"status":"0"},
              "transfer_hook":[],
              "transfer_hook_upgradable":{"authority":[],"status":"0"},
              "trusted_token":1}}}
            """;
        const string usdcMint = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
        var client = CreateClient(new StubHandler(_ => Json(json)));

        var security = await client.GetSolanaTokenSecurityAsync(usdcMint);

        Assert.AreEqual("USDC", security!.Metadata!.Symbol);
        Assert.AreEqual(1, security.TrustedToken);
        Assert.AreEqual(0, security.NonTransferable);
        Assert.AreEqual(1, security.DefaultAccountState);
        Assert.AreEqual(1, security.Mintable!.Status);
        Assert.AreEqual(0, security.Mintable.Authority!.Single().MaliciousAddress);
        Assert.AreEqual(0, security.BalanceMutableAuthority!.Status);
        Assert.IsEmpty(security.TransferHook!);
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_TrustedTokenRealResponseShape_Deserializes()
    {
        // 取自 GoPlus 真实响应（USDT，裁剪了 holders 长数组）：
        // trust_list 是字符串 "1"、is_in_cex 是对象——旧模型把 trust_list 声明为数组，知名代币整体解析失败
        const string json = """
            {"code":1,"message":"OK","result":{"0xdac17f958d2ee523a2206206994597c13d831ec7":{
              "anti_whale_modifiable":"0","buy_tax":"0","can_take_back_ownership":"0","cannot_buy":"0",
              "creator_address":"0x36928500bc1dcd7af6a2b4008875cc336b927d57","creator_percent":"0.000000","external_call":"1","hidden_owner":"0",
              "holder_count":"15892705",
              "holders":[{"address":"0xf977814e90da44bfa03b6295a0616a897441acec","tag":"","is_contract":0,"balance":"13999999999.160432","percent":"0.17","is_locked":0}],
              "honeypot_with_same_creator":"0","is_anti_whale":"0","is_blacklisted":"1","is_honeypot":"0",
              "is_in_cex":{"listed":"1","cex_list":["Binance","Coinbase"]},"is_in_dex":"1","is_mintable":"1",
              "is_open_source":"1","is_proxy":"0","is_whitelisted":"0",
              "owner_address":"0xc6cde7c39eb2f0f0095f41570af89efc2c1ea828","owner_change_balance":"1","owner_percent":"0.000001",
              "personal_slippage_modifiable":"0","selfdestruct":"0","sell_tax":"0","slippage_modifiable":"1",
              "token_name":"Tether USD","token_symbol":"USDT","trading_cooldown":"0","transfer_pausable":"1","transfer_tax":"",
              "trust_list":"1"}}}
            """;
        var client = CreateClient(new StubHandler(_ => Json(json)));

        var security = await client.GetTokenSecurityAsync("ethereum", "0xdac17f958d2ee523a2206206994597c13d831ec7");

        Assert.AreEqual("USDT", security!.TokenSymbol);
        Assert.AreEqual("1", security.TrustList);
        Assert.AreEqual("1", security.IsInCex!.Listed);
        Assert.AreEqual("1", security.OwnerChangeBalance);
        Assert.AreEqual("1", security.IsBlacklisted);
        Assert.AreEqual("0xc6cde7c39eb2f0f0095f41570af89efc2c1ea828", security.OwnerAddress);
        Assert.AreEqual("Tether USD", security.TokenName);
        Assert.AreEqual("0.000001", security.OwnerPercent);
        Assert.AreEqual("0.000000", security.CreatorPercent);
    }

    [TestMethod]
    public async Task GetTokenSecurityAsync_FakeTokenObject_Deserializes()
    {
        // fake_token 仅在检出仿冒主流代币时返回，形态为对象（value 为数字）；
        // 旧模型把它声明为字符串，恰恰会让最需要告警的仿冒币整体解析失败
        const string json = """
            {"code":1,"result":{"0xabc":{"token_symbol":"USDC",
              "fake_token":{"true_token_address":"0xaf88d065e77c8cc2239327c5edb3a432268e5831","value":1}}}}
            """;
        var client = CreateClient(new StubHandler(_ => Json(json)));

        var security = await client.GetTokenSecurityAsync("arbitrum", "0xabc");

        Assert.AreEqual(1, security!.FakeToken!.Value);
        Assert.AreEqual("0xaf88d065e77c8cc2239327c5edb3a432268e5831", security.FakeToken.TrueTokenAddress);
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
        Assert.IsEmpty(handler.RequestUrls);
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
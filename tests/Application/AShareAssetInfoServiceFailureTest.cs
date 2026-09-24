using System.Net;
using System.Text;
using MarketAssistant.Applications.Assets;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// AShareAssetInfoService 失败契约单元测试：数据源不可用必须抛错（不得伪装成"没有结果/未知股票"），
/// 而"确实查不到这只股票"仍返回默认 AssetInfo（桩 HttpMessageHandler，不访问真实网络）。
/// </summary>
[TestClass]
public class AShareAssetInfoServiceFailureTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AShareAssetInfoService CreateService(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://www.cls.cn/") });
        return new AShareAssetInfoService(
            new ClsQuoteClient(factory.Object),
            new SinaFundFlowClient(factory.Object),
            NullLogger<AShareAssetInfoService>.Instance);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SearchAsync_WhenSourceFails_ShouldThrowInsteadOfReturningEmpty()
    {
        var service = CreateService(new StubHandler(_ => Json("{}", HttpStatusCode.InternalServerError)));

        var exception = await Assert.ThrowsExactlyAsync<FriendlyException>(() => service.SearchAsync("茅台"));

        StringAssert.Contains(exception.Message, "搜索股票失败");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SearchAsync_WhenCancelled_ShouldPropagateCancellationInsteadOfFriendlyException()
    {
        using var cts = new CancellationTokenSource();
        // .NET 10 的 HttpClient 只在真实 I/O 期间观察取消，故由桩处理器模拟"请求途中被取消"
        var service = CreateService(new StubHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));

        // 取消必须原样上抛，不能降级成 FriendlyException（否则会被当成失败态提示用户）
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync("茅台", cts.Token));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SearchAsync_WithMatches_ShouldStripHighlightTagsAndMapNameAndCode()
    {
        const string payload = """
            {"data":{"stock":{"data":[{"title":"贵州<em>茅台</em>","stock_id":"sh600519"}]}}}
            """;
        var service = CreateService(new StubHandler(_ => Json(payload)));

        var results = await service.SearchAsync("茅台");

        Assert.HasCount(1, results);
        Assert.AreEqual("贵州茅台", results[0].Name);
        Assert.AreEqual("sh600519", results[0].Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SearchAsync_WithNoMatches_ShouldReturnEmptyListWithoutThrowing()
    {
        var service = CreateService(new StubHandler(_ => Json("""{"data":{"stock":{"data":[]}}}""")));

        var results = await service.SearchAsync("不存在");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfoAsync_WhenSourceFails_ShouldThrowInsteadOfReturningUnknownStock()
    {
        var service = CreateService(new StubHandler(_ => Json("{}", HttpStatusCode.InternalServerError)));

        var exception = await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetAssetInfoAsync("600519", "SH"));

        StringAssert.Contains(exception.Message, "获取股票信息失败");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfoAsync_WithNullData_ShouldReturnUnknownStockWithoutThrowing()
    {
        // data 为 null 表示"该代码无行情"，属于合法空结果，不应升级为错误
        var service = CreateService(new StubHandler(_ => Json("""{"data":null}""")));

        var assetInfo = await service.GetAssetInfoAsync("600519", "SH");

        Assert.IsNotNull(assetInfo);
        Assert.AreEqual("600519", assetInfo.Code);
        Assert.AreEqual("未知股票", assetInfo.Name);
        Assert.AreEqual(MarketType.AShare, assetInfo.MarketType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfoAsync_WithQuoteData_ShouldMapNameCodeAndChangePercentage()
    {
        const string payload = """
            {"data":{"secu_name":"贵州茅台","secu_code":"SH600519","last_px":"1688.80","change":"-0.0082"}}
            """;
        var service = CreateService(new StubHandler(_ => Json(payload)));

        var assetInfo = await service.GetAssetInfoAsync("600519", "SH");

        Assert.AreEqual("贵州茅台", assetInfo.Name);
        Assert.AreEqual("600519", assetInfo.Code, "CLS 返回的 SH/SZ 前缀应被剥离");
        Assert.AreEqual("SH", assetInfo.Market);
        Assert.AreEqual("-0.82%", assetInfo.ChangePercentage);
    }
}

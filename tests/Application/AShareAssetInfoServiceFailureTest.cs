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

    /// <summary>新浪 hq 接口返回 GBK 文本，桩必须按同一编码回写，否则中文名称会解成乱码。</summary>
    private static HttpResponseMessage Gbk(string text)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.GetEncoding("gbk").GetBytes(text))
        };
    }

    private static AShareAssetInfoService CreateService(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        // 两个数据源共用同一套客户端工厂：按命名客户端给出各自的服务域，桩才能区分请求来源
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns((string name) => new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = name.StartsWith("Sina", StringComparison.Ordinal)
                    ? new Uri("https://hq.sinajs.cn/")
                    : new Uri("https://www.cls.cn/")
            });
        return new AShareAssetInfoService(
            new ClsQuoteClient(factory.Object),
            new SinaStockQuoteClient(factory.Object),
            new SinaFundFlowClient(factory.Object),
            NullLogger<AShareAssetInfoService>.Instance);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SearchAsync_WhenSourceFails_ShouldThrowInsteadOfReturningEmpty()
    {
        var service = CreateService(new StubHandler(_ => Json("{}", HttpStatusCode.InternalServerError)));

        var exception = await Assert.ThrowsExactlyAsync<FriendlyException>(() => service.SearchAsync("茅台"));

        Assert.Contains("搜索股票失败", exception.Message);
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

        Assert.Contains("获取股票信息失败", exception.Message);
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

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfoAsync_WhenLastPriceAndChangeAreNull_ShouldNotThrowAndLeavePriceEmpty()
    {
        // 复现线上 JsonException 崩溃：CLS 对停牌/无成交股票返回 "last_px":null、"change":null。
        // 合法"无数据"状态不得抛错，也不得把缺失伪造成 0 价格或 0.00%
        const string payload = """
            {"data":{"secu_name":"测试股票","secu_code":"SH600000","last_px":null,"change":null}}
            """;
        var service = CreateService(new StubHandler(_ => Json(payload)));

        var assetInfo = await service.GetAssetInfoAsync("600000", "SH");

        Assert.AreEqual("测试股票", assetInfo.Name);
        Assert.AreEqual("600000", assetInfo.Code);
        Assert.AreEqual(string.Empty, assetInfo.CurrentPrice, "无行情时应留空而非伪造价格");
        Assert.AreEqual(string.Empty, assetInfo.ChangePercentage, "无涨跌幅时应留空而非显示 0.00%");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfoAsync_WhenClsDoesNotCoverSymbol_ShouldFallBackToSina()
    {
        // 财联社对基金/ETF 不覆盖：data 非空但所有字段为 null（实测 sh511010）
        const string clsPayload = """
            {"data":{"secu_name":null,"secu_code":null,"last_px":null,"change":null}}
            """;
        const string sinaPayload = """
            var hq_str_sh511010="国债ETF国泰,141.370,141.363,141.339,141.405,141.317,141.339,141.344,67106300,9488412378.000,6800,141.339,20300,141.338,1600,141.337,800,141.336,85400,141.334,50000,141.344,1000,141.346,3500,141.347,44400,141.348,77900,141.350,2026-11-13,15:34:59,00,D|1800|254410.20";
            """;

        var service = CreateService(new StubHandler(request =>
            request.RequestUri!.ToString().Contains("hq.sinajs.cn", StringComparison.Ordinal)
                ? Gbk(sinaPayload)
                : Json(clsPayload)));

        var assetInfo = await service.GetAssetInfoAsync("511010", "SH");

        Assert.AreEqual("国债ETF国泰", assetInfo.Name, "名称应来自新浪兜底");
        Assert.AreEqual("511010", assetInfo.Code);
        Assert.AreEqual("SH", assetInfo.Market);
        Assert.AreEqual("141.339", assetInfo.CurrentPrice, "新浪不返回涨跌幅路径下的价格仍应展示；ETF 最小报价单位 0.001，取 3 位");
        Assert.AreEqual("-0.02%", assetInfo.ChangePercentage, "涨跌幅应按（最新-昨收）/昨收 现算");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfoAsync_WhenSuspended_ShouldKeepNameAndNotBorrowSinaPrice()
    {
        // 财联社有名称但无最新价 = 停牌：不得用新浪行情伪装成实时价
        const string clsPayload = """
            {"data":{"secu_name":"测试股票","secu_code":"SH600000","last_px":null,"change":null}}
            """;
        const string sinaPayload = """
            var hq_str_sh600000="浦发银行,10.000,10.000,10.500,10.600,9.900,10.500,10.510,1000000,10500000.000,800,10.500,900,10.499,700,10.498,600,10.497,500,10.496,700,10.501,800,10.502,900,10.503,600,10.504,500,10.505,2026-11-13,15:00:00,00";
            """;

        var service = CreateService(new StubHandler(request =>
            request.RequestUri!.ToString().Contains("hq.sinajs.cn", StringComparison.Ordinal)
                ? Gbk(sinaPayload)
                : Json(clsPayload)));

        var assetInfo = await service.GetAssetInfoAsync("600000", "SH");

        Assert.AreEqual("测试股票", assetInfo.Name);
        Assert.AreEqual(string.Empty, assetInfo.CurrentPrice, "停牌时应留空，不得回退新浪价格");
    }
}

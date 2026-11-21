using System.Diagnostics;
using System.Net;
using MarketAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace TestMarketAssistant.Infrastructure;

/// <summary>
/// 智兔命名 HttpClient 的弹性策略契约测试。
/// </summary>
/// <remarks>
/// 智兔对不覆盖的标的（基金/ETF 等）返回 503 <c>redis history miss</c>，语义等同 404——
/// 是「没有数据」而非瞬时抖动。默认 StandardResilienceHandler 的 3 次指数退避（1s/3s/7s）
/// 会把一次确定失败放大成约 15 秒等待，期间详情页 K 线区一直空着。
/// 本测试锁定「失败快速交给兜底数据源」这一契约，防止后续调整重试参数时悄悄退化。
/// </remarks>
[TestClass]
public sealed class ZhiTuResiliencePolicyTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task ZhiTuClient_OnPersistent503_ShouldRetryOnceAndFailFast()
    {
        var handler = new CountingStatusHandler(HttpStatusCode.ServiceUnavailable);
        var services = new ServiceCollection();
        services.AddNamedMarketHttpClients();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new ReplacePrimaryHandlerOnly("ZhiTu", handler));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ZhiTu");

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync("https://api.zhituapi.com/hs/history/511010.SH/d/n");
        stopwatch.Stop();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // 1 次原始尝试 + 1 次重试：真实网络抖动仍有一次补救机会
        Assert.AreEqual(2, handler.CallCount, "智兔只应重试 1 次");

        // 固定 500ms 退避，不再是指数退避叠加出的十余秒等待
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"确定失败应在 5 秒内交出，实际 {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SinaKLineClient_ShouldKeepDefaultRetries()
    {
        // 兜底数据源不跟着收紧：它是最后一道防线，多一次重试比快速失败更有价值
        var handler = new CountingStatusHandler(HttpStatusCode.ServiceUnavailable);
        var services = new ServiceCollection();
        services.AddNamedMarketHttpClients();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new ReplacePrimaryHandlerOnly("SinaKLine", handler));

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("SinaKLine");

        using var response = await client.GetAsync("https://quotes.sina.cn/cn/api/json_v2.php");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsGreaterThan(2, handler.CallCount, "兜底数据源应保留默认的多次重试");
    }

    /// <summary>
    /// 只替换主处理器，保留弹性管线（AdditionalHandlers）。
    /// 复用测试里常见的 ReplacePrimaryHandlerFilter 会连带清空 AdditionalHandlers，那样测不到重试策略本身。
    /// </summary>
    private sealed class ReplacePrimaryHandlerOnly(
        string clientName,
        HttpMessageHandler handler) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);
                if (builder.Name == clientName)
                {
                    builder.PrimaryHandler = handler;
                }
            };
        }
    }

    private sealed class CountingStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode) { RequestMessage = request });
        }
    }
}

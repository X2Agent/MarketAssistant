using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// 新浪数据客户端真实接口集成测试（Integration 类别，CI 默认排除，本地按需运行）。
/// </summary>
/// <remarks>
/// 存在意义：这两个客户端面对的是无官方文档、契约易变的非公开接口。
/// 单元测试只能验证"已知载荷"的解析；本测试守住真实往返，
/// 曾在开发期抓到 <c>quotes.sina.cn</c> K 线接口 <b>Content-Type 谎报 charset=gbk</b>
/// （实际 body 为 UTF-8）导致 <c>GetStringAsync</c> 直接抛异常的线上级问题。
/// </remarks>
[TestClass]
public class SinaLiveApiIntegrationTest
{
    public TestContext TestContext { get; set; } = null!;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAShareDataProviders();
        services.AddHttpClient("SinaHq", client =>
        {
            client.BaseAddress = new Uri("https://hq.sinajs.cn");
            client.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn/");
        });
        services.AddHttpClient("SinaKLine", client =>
        {
            client.BaseAddress = new Uri("https://quotes.sina.cn");
            client.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn/");
        });
        return services.BuildServiceProvider();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task KLine_ShouldReturnDataForEtfStockAndMinuteScale()
    {
        using var sp = BuildProvider();
        var client = sp.GetRequiredService<SinaKLineClient>();

        var etfBars = await client.GetKLineBarsAsync("sh511010", 240, 5);
        Assert.IsTrue(etfBars.Count > 0, "ETF（智兔不覆盖）应经新浪 K 线返回数据");
        TestContext.WriteLine($"ETF 日线 {etfBars.Count} 根, 最新 {etfBars[^1].Day} close={etfBars[^1].Close}");

        var stockBars = await client.GetKLineBarsAsync("sz300308", 240, 3);
        Assert.IsTrue(stockBars.Count > 0, "股票应经新浪 K 线返回数据");

        var minuteBars = await client.GetKLineBarsAsync("sh511010", 15, 3);
        Assert.IsTrue(minuteBars.Count > 0, "分钟线应经新浪 K 线返回数据");
        Assert.IsTrue(minuteBars[^1].Amount > 0m, "分钟线响应含成交额");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task KLine_InvalidSymbol_ShouldReturnEmptyList()
    {
        using var sp = BuildProvider();
        var client = sp.GetRequiredService<SinaKLineClient>();

        // 无效代码时接口返回 JSON null（非数组），必须按空列表处理而非抛异常
        Assert.IsEmpty(await client.GetKLineBarsAsync("sh999999", 240, 5));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task StockQuote_ShouldParseWhenMarketHasData()
    {
        using var sp = BuildProvider();
        var client = sp.GetRequiredService<SinaStockQuoteClient>();

        var quote = await client.GetStockQuoteAsync("sh600519");

        if (quote is null)
        {
            // 盘前/停牌时段接口返回全 0，解析器按交叉校验策略拒绝，属预期防御行为
            TestContext.WriteLine("当前时段无有效行情（盘前/停牌），解析器返回 null，符合防御预期");
            return;
        }

        Assert.AreEqual(5, quote.Bids.Count);
        Assert.AreEqual(5, quote.Asks.Count);
        Assert.IsTrue(quote.CurrentPrice > 0m);
        TestContext.WriteLine($"盘口 {quote.Name} price={quote.CurrentPrice} " +
            $"open={quote.Open} preclose={quote.PreClose} high={quote.High} low={quote.Low} " +
            $"vol={quote.Volume} amount={quote.Amount}");
    }
}

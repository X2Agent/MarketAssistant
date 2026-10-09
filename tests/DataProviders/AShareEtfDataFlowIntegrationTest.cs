using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Technical;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// A 股基金/ETF 数据链路集成测试（Integration 类别，CI 默认排除，本地按需运行）。
/// </summary>
/// <remarks>
/// 复现并守护线上问题：智兔 K 线不覆盖基金/ETF（404），曾导致资产详情页
/// K 线与技术指标同时失败。本测试在不配置智兔令牌的前提下验证
/// "K 线回退新浪 → 技术指标可计算"的完整链路。
/// </remarks>
[TestClass]
public class AShareEtfDataFlowIntegrationTest
{
    public TestContext TestContext { get; set; } = null!;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAShareDataProviders();
        services.AddHttpClient("SinaHq", client =>
        {
            client.BaseAddress = new Uri("https://hq.sinajs.cn");
            client.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn/");
        });
        services.AddHttpClient("SinaKLine", client =>
        {
            client.BaseAddress = new Uri("https://quotes.sina.cn");
        });
        services.AddHttpClient("ZhiTu", client =>
        {
            client.BaseAddress = new Uri("https://api.zhituapi.com");
        });

        // 不配置智兔令牌：智兔必然失败，强制走新浪兜底路径
        var userSetting = new Mock<IUserSettingService>();
        userSetting.Setup(s => s.CurrentSetting).Returns(new UserSetting { ZhiTuApiToken = "" });
        services.AddSingleton(userSetting.Object);

        services.AddSingleton<IKLineService, MarketAssistant.Applications.Charts.AShareKLineService>();
        services.AddSingleton<ITechnicalIndicatorService, TechnicalIndicatorService>();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task EtfKLine_WithoutZhiTuToken_ShouldFallBackToSina()
    {
        using var sp = BuildProvider();
        var klineService = sp.GetRequiredService<IKLineService>();

        var klineData = await klineService.GetKLineDataAsync("sh511010", KLineType.Daily, 250);

        Assert.IsTrue(klineData.Count > 100, $"ETF K 线应有充足数据，实际 {klineData.Count}");
        var latest = klineData[^1];
        TestContext.WriteLine(
            $"K线 {klineData.Count} 根, 最新 {latest.Timestamp:yyyy-MM-dd} close={latest.Close} " +
            $"preClose={latest.PreClose} change={latest.Change} pct={latest.PctChg:F2}%");

        Assert.IsTrue(latest.Close > 0m, "收盘价应为正");
        Assert.IsTrue(klineData[^2].PreClose > 0m, "昨收应由前一根收盘价派生");
        Assert.AreNotEqual(0m, latest.Change, "涨跌额应由派生的昨收计算");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task EtfTechnicalIndicator_WithoutZhiTuToken_ShouldComputeFromFallbackData()
    {
        using var sp = BuildProvider();
        var indicatorService = sp.GetRequiredService<ITechnicalIndicatorService>();

        var snapshot = await indicatorService.GetSnapshotAsync("sh511010", KLineType.Daily, 250);

        Assert.IsTrue(snapshot.GetMovingAverage(5) > 0m, "MA5 应可计算");
        Assert.IsTrue(snapshot.GetMovingAverage(20) > 0m, "MA20 应可计算");
        Assert.IsNotNull(snapshot.MacdDiff, "MACD 应可计算");
        Assert.IsNotNull(snapshot.BollUpper, "BOLL 应可计算");
        Assert.IsNotNull(snapshot.KdjK, "KDJ 应可计算");
        TestContext.WriteLine(
            $"指标 MA5={snapshot.GetMovingAverage(5)} MA20={snapshot.GetMovingAverage(20)} " +
            $"DIF={snapshot.MacdDiff} DEA={snapshot.MacdDea} BOLL上={snapshot.BollUpper} K={snapshot.KdjK}");
    }
}

using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.ViewModels;

namespace TestMarketAssistant.Application;

/// <summary>
/// <see cref="AlertHistoryFilter"/> 单元测试：级别过滤与标的关键词匹配
/// （代码或标题，忽略大小写），空关键词视为不限。
/// </summary>
[TestClass]
public sealed class AlertHistoryFilterTest
{
    private static AlertEvent CreateAlert(
        string symbol,
        string title,
        AlertLevel level = AlertLevel.Warning,
        MarketType marketType = MarketType.AShare)
        => new()
        {
            Symbol = symbol,
            Title = title,
            Level = level,
            MarketType = marketType
        };

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_NoFilters_ShouldMatchAnyAlert()
    {
        var alert = CreateAlert("SH600519", "贵州茅台价格突破", AlertLevel.Critical);

        Assert.IsTrue(AlertHistoryFilter.Matches(alert, null, null));
        Assert.IsTrue(AlertHistoryFilter.Matches(alert, null, "   "));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_LevelFilter_ShouldOnlyKeepSameLevel()
    {
        var critical = CreateAlert("SH600519", "价格突破", AlertLevel.Critical);
        var info = CreateAlert("SZ000001", "信号变化", AlertLevel.Info);

        Assert.IsTrue(AlertHistoryFilter.Matches(critical, AlertLevel.Critical, null));
        Assert.IsFalse(AlertHistoryFilter.Matches(critical, AlertLevel.Warning, null));
        Assert.IsFalse(AlertHistoryFilter.Matches(info, AlertLevel.Critical, null));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_Keyword_ShouldMatchSymbolOrTitleIgnoringCase()
    {
        var alert = CreateAlert("SH600519", "贵州茅台触及目标价", AlertLevel.Warning);

        Assert.IsTrue(AlertHistoryFilter.Matches(alert, null, "600519"));
        Assert.IsTrue(AlertHistoryFilter.Matches(alert, null, "茅台"));
        Assert.IsTrue(AlertHistoryFilter.Matches(alert, null, "sh600519"));
        Assert.IsTrue(AlertHistoryFilter.Matches(alert, null, "SH600519"));
        Assert.IsFalse(AlertHistoryFilter.Matches(alert, null, "比特币"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_LevelAndKeywordCombined_ShouldRequireBoth()
    {
        var alert = CreateAlert("BTCUSDT", "比特币剧烈波动", AlertLevel.Critical);

        Assert.IsTrue(AlertHistoryFilter.Matches(alert, AlertLevel.Critical, "btc"));
        Assert.IsFalse(AlertHistoryFilter.Matches(alert, AlertLevel.Warning, "btc"));
        Assert.IsFalse(AlertHistoryFilter.Matches(alert, AlertLevel.Critical, "茅台"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_NullAlert_ShouldThrow()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => AlertHistoryFilter.Matches(null!, null, null));
    }
}

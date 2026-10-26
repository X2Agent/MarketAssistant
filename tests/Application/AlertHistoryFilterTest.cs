using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.ViewModels;
using Moq;

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

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_MarketFilter_ShouldOnlyKeepSameMarket()
    {
        var aShare = CreateAlert("SH600519", "茅台异动", AlertLevel.Warning, MarketType.AShare);
        var crypto = CreateAlert("BTCUSDT", "比特币异动", AlertLevel.Warning, MarketType.Crypto);

        Assert.IsTrue(AlertHistoryFilter.Matches(aShare, null, null, MarketType.AShare));
        Assert.IsFalse(AlertHistoryFilter.Matches(crypto, null, null, MarketType.AShare));
        Assert.IsTrue(AlertHistoryFilter.Matches(crypto, null, null, MarketType.Crypto));
        Assert.IsFalse(AlertHistoryFilter.Matches(aShare, null, null, MarketType.Crypto));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_NullMarket_ShouldNotFilterByMarket()
    {
        // "全部市场"必须等价于不筛选，否则默认选项就会隐藏一半告警
        var aShare = CreateAlert("SH600519", "茅台异动", marketType: MarketType.AShare);
        var crypto = CreateAlert("BTCUSDT", "比特币异动", marketType: MarketType.Crypto);

        Assert.IsTrue(AlertHistoryFilter.Matches(aShare, null, null, null));
        Assert.IsTrue(AlertHistoryFilter.Matches(crypto, null, null, null));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_MarketAndKeywordCombined_ShouldRequireBoth()
    {
        // 关键回归点：市场过滤不能绕过关键词，二者是 AND 关系
        var crypto = CreateAlert("BTCUSDT", "比特币异动", AlertLevel.Warning, MarketType.Crypto);

        Assert.IsTrue(AlertHistoryFilter.Matches(crypto, null, "btc", MarketType.Crypto));
        Assert.IsFalse(AlertHistoryFilter.Matches(crypto, null, "茅台", MarketType.Crypto),
            "市场匹配但关键词不匹配应被过滤掉");
        Assert.IsFalse(AlertHistoryFilter.Matches(crypto, null, "btc", MarketType.AShare),
            "关键词匹配但市场不匹配应被过滤掉");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Matches_LevelMarketKeywordAllCombined_ShouldRequireAll()
    {
        var alert = CreateAlert("BTCUSDT", "比特币触及目标价", AlertLevel.Critical, MarketType.Crypto);

        Assert.IsTrue(AlertHistoryFilter.Matches(alert, AlertLevel.Critical, "btc", MarketType.Crypto));
        Assert.IsFalse(AlertHistoryFilter.Matches(alert, AlertLevel.Info, "btc", MarketType.Crypto));
        Assert.IsFalse(AlertHistoryFilter.Matches(alert, AlertLevel.Critical, "btc", MarketType.AShare));
        Assert.IsFalse(AlertHistoryFilter.Matches(alert, AlertLevel.Critical, "茅台", MarketType.Crypto));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MarketFilters_ShouldIncludeAllOptionFirst()
    {
        // 默认选中第一项；若集合顺序变化会导致默认行为从"全部"变成"仅 A 股"。
        // 用 Moq 构造 ViewModel，避免为这一个断言手写整个 IAlertCenterService 桩。
        var alertCenter = new Mock<IAlertCenterService>();
        alertCenter
            .Setup(service => service.GetRecentAlertsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AlertEvent>)[]);

        using var vm = new AlertHistoryViewModel(alertCenter.Object);

        Assert.AreEqual(3, vm.MarketFilters.Count, "应为 全部/A股/虚拟币 三项");
        Assert.IsNull(vm.MarketFilters[0].MarketType, "首项必须是「全部市场」");
        Assert.IsNull(vm.SelectedMarketFilter?.MarketType, "默认选中全部市场");
    }
}

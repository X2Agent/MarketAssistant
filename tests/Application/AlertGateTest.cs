using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class AlertGateTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Activate_ShouldGateSymbolUntilDeactivated()
    {
        var gate = new AlertGate();

        Assert.IsFalse(gate.IsGated(MarketType.Crypto, "BTCUSDT"));

        gate.Activate(MarketType.Crypto, "btcusdt");
        Assert.IsTrue(gate.IsGated(MarketType.Crypto, "BTCUSDT"));

        gate.Deactivate(MarketType.Crypto, "BTCUSDT");
        Assert.IsFalse(gate.IsGated(MarketType.Crypto, "BTCUSDT"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MultipleActivations_ShouldRequireMatchingDeactivations()
    {
        var gate = new AlertGate();

        gate.Activate(MarketType.Crypto, "BTCUSDT");
        gate.Activate(MarketType.Crypto, "BTCUSDT");
        gate.Deactivate(MarketType.Crypto, "BTCUSDT");

        // 还有一条生效中的告警，仍处于联动状态
        Assert.IsTrue(gate.IsGated(MarketType.Crypto, "BTCUSDT"));

        gate.Deactivate(MarketType.Crypto, "BTCUSDT");
        Assert.IsFalse(gate.IsGated(MarketType.Crypto, "BTCUSDT"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Gate_ShouldBePerMarketAndSymbol()
    {
        var gate = new AlertGate();

        gate.Activate(MarketType.Crypto, "BTCUSDT");

        Assert.IsFalse(gate.IsGated(MarketType.AShare, "BTCUSDT"));
        Assert.IsFalse(gate.IsGated(MarketType.Crypto, "ETHUSDT"));
        Assert.IsFalse(gate.IsGated(MarketType.Crypto, ""));
    }
}

[TestClass]
public sealed class AShareTradingHoursTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Weekend_ShouldNotBeTradingSession()
    {
        // 2026-01-03（周六）14:00 北京时间 = 06:00 UTC
        var saturdayAfternoonUtc = new DateTime(2026, 1, 3, 6, 0, 0, DateTimeKind.Utc);

        Assert.IsFalse(AShareTradingHours.IsTradingSession(saturdayAfternoonUtc));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MorningSession_ShouldBeTradingSession()
    {
        // 2026-01-05（周一）10:00 北京时间 = 02:00 UTC
        var mondayMorningUtc = new DateTime(2026, 1, 5, 2, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(AShareTradingHours.IsTradingSession(mondayMorningUtc));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LunchBreak_ShouldNotBeTradingSession()
    {
        // 2026-01-05（周一）12:30 北京时间 = 04:30 UTC
        var lunchBreakUtc = new DateTime(2026, 1, 5, 4, 30, 0, DateTimeKind.Utc);

        Assert.IsFalse(AShareTradingHours.IsTradingSession(lunchBreakUtc));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AfternoonSession_ShouldBeTradingSession()
    {
        // 2026-01-05（周一）14:59 北京时间 = 06:59 UTC
        var nearCloseUtc = new DateTime(2026, 1, 5, 6, 59, 0, DateTimeKind.Utc);

        Assert.IsTrue(AShareTradingHours.IsTradingSession(nearCloseUtc));

        // 15:01 已收盘
        var afterCloseUtc = new DateTime(2026, 1, 5, 7, 1, 0, DateTimeKind.Utc);
        Assert.IsFalse(AShareTradingHours.IsTradingSession(afterCloseUtc));
    }
}

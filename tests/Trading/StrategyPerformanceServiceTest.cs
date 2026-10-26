using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 策略绩效统计测试：胜率、盈亏比、最大回撤口径。
/// </summary>
[TestClass]
public sealed class StrategyPerformanceServiceTest
{
    private static StrategyPerformanceService CreateService(List<TradeRecord> records)
    {
        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService
            .Setup(service => service.GetRecordsByStrategyAsync("strategy-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);
        dataService
            .Setup(service => service.GetAllStrategiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TradingStrategy { Id = "strategy-1", Symbol = "BTCUSDT" }]);

        var shadowService = new ShadowTradingService(
            dataService.Object, NullLogger<ShadowTradingService>.Instance);


        return new StrategyPerformanceService(
            dataService.Object, shadowService, NullLogger<StrategyPerformanceService>.Instance);
    }

    private static TradeRecord CreateClose(DateTime createdAt, decimal pnl) => new()
    {
        StrategyId = "strategy-1",
        Symbol = "BTCUSDT",
        Side = OrderSide.Sell,
        ExecutedQty = 0.1m,
        ExecutedPrice = 100m,
        RealizedPnl = pnl,
        CreatedAt = createdAt
    };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAsync_WithoutClosingTrades_ShouldReturnNull()
    {
        var service = CreateService([CreateClose(DateTime.UtcNow, 0m)]);

        Assert.IsNull(await service.GetAsync("strategy-1"), "没有已实现盈亏的成交时无绩效可言");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAsync_ShouldComputeWinRateProfitFactorAndDrawdown()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var records = new List<TradeRecord>
        {
            // +100 → 累计 100（峰 100）
            CreateClose(baseTime, 100m),
            // -30 → 累计 70（回撤 30）
            CreateClose(baseTime.AddHours(1), -30m),
            // +50 → 累计 120（峰 120）
            CreateClose(baseTime.AddHours(2), 50m),
            // -20 → 累计 100（回撤 20）
            CreateClose(baseTime.AddHours(3), -20m)
        };

        var service = CreateService(records);
        var performance = await service.GetAsync("strategy-1");

        Assert.IsNotNull(performance);
        Assert.AreEqual(4, performance!.ClosedTrades);
        Assert.AreEqual(2, performance.WinningTrades);
        Assert.AreEqual(100m, performance.TotalPnl);
        Assert.AreEqual(50m, performance.WinRate);
        // 盈亏比 = 150 / 50 = 3
        Assert.AreEqual(3m, performance.ProfitFactor!.Value, 0.0001m);
        // 最大回撤 = 第二笔后的峰谷差（100 → 70 = 30），
        // 后续创新高只是让"当前回撤"变小，不改变历史最大回撤
        Assert.AreEqual(30m, performance.MaxDrawdown);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAsync_WithoutLosingTrades_ProfitFactorShouldBeNull()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            [CreateClose(baseTime, 10m), CreateClose(baseTime.AddHours(1), 20m)]);

        var performance = await service.GetAsync("strategy-1");

        Assert.IsNull(performance!.ProfitFactor, "无亏损交易时盈亏比无定义");
        Assert.AreEqual(0m, performance.MaxDrawdown, "单向盈利不构成回撤");
        Assert.AreEqual(30m, performance.TotalPnl);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAllAsync_ShouldSkipStrategiesWithoutClosingTrades()
    {
        var service = CreateService([CreateClose(DateTime.UtcNow, 0m)]);

        Assert.IsEmpty(await service.GetAllAsync());
    }
}

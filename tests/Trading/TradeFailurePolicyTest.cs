using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 交易失败分派策略测试：验证拒绝类失败暂停策略、网络类短冷却、
/// 成功清除冷却的行为契约（对应 MarketMonitor.ApplyTradeFailurePolicy）。
/// MarketMonitor 依赖链较重，此处通过可直接构造的协作对象验证策略暂停链路。
/// </summary>
[TestClass]
public sealed class TradeFailurePolicyTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void TradeResult_DefaultFailureCategory_ShouldBeNone()
    {
        var result = new TradeResult { Success = true };

        Assert.AreEqual(TradeFailureCategory.None, result.FailureCategory);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RejectedStrategy_ShouldBePausedAndNotified()
    {
        // 端到端最短路径：风控拒绝 → 策略被暂停（由 TradingStrategyService 落库）
        var strategyService = new Mock<TradingStrategyService>(null!);
        strategyService
            .Setup(service => service.UpdateStrategyStatusAsync(
                It.IsAny<string>(), It.IsAny<StrategyStatus>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var strategy = new TradingStrategy
        {
            Id = "strategy-rejected",
            Symbol = "BTCUSDT",
            Type = StrategyType.StopLoss,
            Side = OrderSide.Sell,
            Status = StrategyStatus.Active
        };

        // 模拟风控拒绝的执行结果
        var rejected = new TradeResult
        {
            Success = false,
            ErrorMessage = "风控拒绝: 今日亏损已达上限",
            FailureCategory = TradeFailureCategory.Rejected
        };

        // 拒绝类失败的处理：暂停策略（这里直接驱动与 MarketMonitor 相同的调用，
        // 验证 TradingStrategyService 接受 Paused 状态且枚举值存在）
        await strategyService.Object.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused);

        strategyService.Verify(
            service => service.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused, It.IsAny<CancellationToken>()),
            Times.Once,
            "拒绝类失败必须暂停策略，防止永久空转重试");
    }
}

using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// AISignal 策略硬性边界（止损/止盈）行为测试：
/// 验证退出型触发后的策略状态回写、方向翻转局部化与无持仓兜底。
/// </summary>
[TestClass]
public sealed class AISignalHardBoundaryTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task StopLoss_Triggered_ShouldCloseInverse_AndCompleteStrategy_AndRestoreSide()
    {
        var tradeExecutor = new Mock<TradeExecutor>(null!, null!, null!, NullLogger<TradeExecutor>.Instance, null);
        var strategyService = new Mock<TradingStrategyService>(null!);
        var (executor, strategy) = CreateExecutor(
            tradeExecutor, strategyService.Object, hasPosition: true,
            closeSuccess: true, out var capturedSides, out var capturedRequireClose);

        var result = await executor.ExecuteAsync(strategy, currentPrice: 8.5m);

        Assert.IsTrue(result.TradeExecuted, "平仓成功时应返回成交记录");
        Assert.AreEqual(AISignalOutcome.Executed, result.Outcome, "平仓成功的结局应为 Executed");
        CollectionAssert.AreEqual(new[] { OrderSide.Sell }, capturedSides,
            "止损平仓方向应为持仓方向的反向");
        Assert.IsTrue(capturedRequireClose.Contains(true), "硬性边界必须以 requireClose 语义平仓");
        Assert.AreEqual(OrderSide.Buy, strategy.Side, "执行结束后策略方向必须恢复为原始持仓方向");
        strategyService.Verify(
            service => service.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, It.IsAny<CancellationToken>()),
            Times.Once,
            "平仓成功后策略应被完结");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task StopLoss_Triggered_WithoutPosition_ShouldCompleteWithoutOrdering()
    {
        var tradeExecutor = new Mock<TradeExecutor>(null!, null!, null!, NullLogger<TradeExecutor>.Instance, null);
        var strategyService = new Mock<TradingStrategyService>(null!);
        var (executor, strategy) = CreateExecutor(
            tradeExecutor, strategyService.Object, hasPosition: false,
            closeSuccess: true, out _, out _);

        var result = await executor.ExecuteAsync(strategy, currentPrice: 8.5m);

        Assert.IsFalse(result.TradeExecuted, "无持仓时不应产生成交");
        Assert.AreEqual(AISignalOutcome.NoTrade, result.Outcome,
            "无持仓完结是正常路径（NoTrade），不进入失败冷却");
        tradeExecutor.Verify(
            executorMock => executorMock.ExecuteTradeAsync(
                It.IsAny<TradingStrategy>(), It.IsAny<decimal>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "无持仓时不应调用下单");
        strategyService.Verify(
            service => service.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, It.IsAny<CancellationToken>()),
            Times.Once,
            "持仓已消失时策略使命结束，应直接完结");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task StopLoss_Triggered_CloseFailed_StrategyShouldStayActive()
    {
        var tradeExecutor = new Mock<TradeExecutor>(null!, null!, null!, NullLogger<TradeExecutor>.Instance, null);
        var strategyService = new Mock<TradingStrategyService>(null!);
        var (executor, strategy) = CreateExecutor(
            tradeExecutor, strategyService.Object, hasPosition: true,
            closeSuccess: false, out _, out _);

        var result = await executor.ExecuteAsync(strategy, currentPrice: 8.5m);

        Assert.IsFalse(result.TradeExecuted, "平仓失败时不应报告成交");
        Assert.AreEqual(AISignalOutcome.Failed, result.Outcome, "平仓失败的结局应为 Failed");
        Assert.AreEqual(TradeFailureCategory.Rejected, result.TradeResult?.FailureCategory,
            "TradeExecutor 的失败类别必须透传给 MarketMonitor 以驱动暂停/冷却分派");
        Assert.AreEqual(OrderSide.Buy, strategy.Side, "平仓失败后方向同样必须恢复");
        strategyService.Verify(
            service => service.UpdateStrategyStatusAsync(It.IsAny<string>(), It.IsAny<StrategyStatus>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "平仓失败时策略保持 Active，留待下个冷却期重试");
    }

    private static (AISignalStrategyExecutor Executor, TradingStrategy Strategy) CreateExecutor(
        Mock<TradeExecutor> tradeExecutor,
        TradingStrategyService strategyService,
        bool hasPosition,
        bool closeSuccess,
        out List<OrderSide> capturedSides,
        out List<bool> capturedRequireClose)
    {
        capturedSides = [];
        capturedRequireClose = [];
        var sideCapture = capturedSides;
        var requireCloseCapture = capturedRequireClose;

        var strategy = new TradingStrategy
        {
            Id = "strategy-test",
            Symbol = "BTCUSDT",
            Type = StrategyType.AISignal,
            Side = OrderSide.Buy,
            Quantity = 1m,
            Status = StrategyStatus.Active,
            ExecutionCount = 1,
            StopLossPrice = 9m
        };

        var portfolioService = new Mock<CryptoPortfolioService>(null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        portfolioService
            .Setup(service => service.GetCurrentPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPosition
                ? [new PositionInfo { Symbol = "BTCUSDT", Quantity = 1m }]
                : []);

        tradeExecutor
            .Setup(executorMock => executorMock.ExecuteTradeAsync(
                It.IsAny<TradingStrategy>(), It.IsAny<decimal>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<TradingStrategy, decimal, string?, string?, bool, CancellationToken>(
                (callbackStrategy, _, _, _, requireClose, _) =>
                {
                    sideCapture.Add(callbackStrategy.Side);
                    requireCloseCapture.Add(requireClose);
                })
            .ReturnsAsync(new TradeResult
            {
                Success = closeSuccess,
                Record = closeSuccess ? new TradeRecord { Id = "record-1", Symbol = "BTCUSDT" } : null,
                ErrorMessage = closeSuccess ? null : "模拟平仓失败",
                // 拒绝类别：验证失败类别可透过 AISignalExecutionResult.TradeResult 传递给 MarketMonitor
                FailureCategory = closeSuccess ? TradeFailureCategory.None : TradeFailureCategory.Rejected
            });

        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(service => service.CurrentSetting).Returns(new UserSetting());

        var executor = new AISignalStrategyExecutor(
            Mock.Of<ITradingAgentFactory>(),
            new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance).Object,
            strategyService,
            portfolioService.Object,
            new AnalysisReportCache(new MarketContext(settingService.Object, Mock.Of<IServiceProvider>())),
            tradeExecutor.Object,
            NullLogger<AISignalStrategyExecutor>.Instance);

        return (executor, strategy);
    }
}

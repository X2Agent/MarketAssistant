using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 并发卖出锁内复检测试：两笔基于同一持仓快照通过风控的现货卖出，
/// 在 symbol 锁上串行化后，第二笔必须被锁内复检拒绝，防止超卖。
/// 同时覆盖 ExtractBaseAsset 解析失败时的 fail-closed 行为。
/// </summary>
[TestClass]
public sealed class TradeExecutorConcurrencyTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task ConcurrentSells_OnSamePosition_SecondOrderShouldBeRejectedByInLockRecheck()
    {
        var exchangeClient = new Mock<IExchangeClient>();
        exchangeClient.Setup(client => client.IsFutures).Returns(false);
        exchangeClient
            .Setup(client => client.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeOrderResult
            {
                OrderId = "1001",
                Status = "FILLED",
                ExecutedQty = 0.6m,
                RequestedQty = 0.6m
            });

        // 风控直接放行：模拟两笔卖出都基于同一持仓快照在锁外通过了风控（旧 A2 竞态前提）
        var riskManager = new Mock<RiskManager>(null!, null!, null!, NullLogger<RiskManager>.Instance);
        riskManager
            .Setup(manager => manager.ValidateOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<OrderType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RiskCheckResult.Pass());

        // 锁内复检读取的本地 FIFO 持仓：第一次 1.0（首单放行），第二次 0.4（首单已消耗 0.6）
        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService
            .SetupSequence(service => service.GetOpenPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreatePosition(1.0m)])
            .ReturnsAsync([CreatePosition(0.4m)]);
        dataService
            .Setup(service => service.SaveTradeRecordAsync(It.IsAny<TradeRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dataService
            .Setup(service => service.ClosePositionFifoAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>(), It.IsAny<PositionSide>()))
            .ReturnsAsync(0m);
        dataService
            .Setup(service => service.UpdateDailyStatsAsync(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var executor = new TradeExecutor(
            exchangeClient.Object,
            riskManager.Object,
            dataService.Object,
            NullLogger<TradeExecutor>.Instance);

        var first = executor.ExecuteOrderAsync("BTCUSDT", OrderSide.Sell, OrderType.Market, 0.6m, 100m);
        var second = executor.ExecuteOrderAsync("BTCUSDT", OrderSide.Sell, OrderType.Market, 0.6m, 100m);
        var results = await Task.WhenAll(first, second);

        var succeeded = results.Count(result => result.Success);
        var rejected = results.Count(result => !result.Success);

        Assert.AreEqual(1, succeeded, "同一持仓的两笔并发卖出只允许一笔成交");
        Assert.AreEqual(1, rejected, "第二笔必须被锁内复检拒绝");
        Assert.IsTrue(
            results.Where(result => !result.Success).Any(result =>
                result.ErrorMessage?.Contains("并发校验失败", StringComparison.Ordinal) == true),
            "拒绝原因应明确指向并发校验");
        exchangeClient.Verify(
            client => client.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "交易所只应收到一笔订单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Sell_OnUnparseableSymbol_RiskCheckShouldFailClosed()
    {
        var exchangeClient = new Mock<IExchangeClient>();
        exchangeClient.Setup(client => client.IsFutures).Returns(false);

        var portfolioService = new Mock<CryptoPortfolioService>(null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        portfolioService
            .Setup(service => service.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new AccountBalanceSummary
            {
                TotalValueUSDT = 1000m,
                Assets =
                [
                    new AssetBalance { Asset = "USDT", Free = 1000m, Locked = 0m, ValueUSDT = 1000m }
                ]
            });

        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService
            .Setup(service => service.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig());
        dataService
            .Setup(service => service.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats());

        // "UNKNOWNQUOTE" 无法从固定报价后缀表解析出基础资产
        var riskManager = new RiskManager(
            dataService.Object,
            portfolioService.Object,
            exchangeClient.Object,
            NullLogger<RiskManager>.Instance);

        var result = await riskManager.ValidateOrderAsync(
            "UNKNOWNQUOTE", OrderSide.Sell, 0.1m, 100m, OrderType.Market);

        Assert.IsFalse(result.Passed, "无法解析基础资产时卖出校验必须 fail-closed");
        StringAssert.Contains(result.Reason ?? string.Empty, "fail-closed");
    }

    private static Position CreatePosition(decimal remainingQuantity)
        => new()
        {
            Symbol = "BTCUSDT",
            Side = PositionSide.Long,
            Quantity = remainingQuantity,
            ClosedQuantity = 0m
        };
}

using System.Net.Http;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// RiskManager 闸门矩阵测试：覆盖熔断类之外的金额/次数/仓位/余额/准入闸门，
/// 以及各闸门的 fail-closed / fail-open 边界行为。
/// 共享 <see cref="CreateContext"/> 构造默认全放行的风控上下文，每个用例只覆写本闸门输入。
/// </summary>
[TestClass]
public sealed class RiskManagerGateTest
{
    private const string Symbol = "BTCUSDT";

    private sealed record RiskContext(
        RiskManager Manager,
        Mock<TradingDataService> Data,
        Mock<CryptoPortfolioService> Portfolio,
        Mock<IExchangeClient> Exchange,
        ApiHealthMonitor ApiHealth);

    private static RiskConfig OpenConfig() => new()
    {
        MaxSingleOrderPercent = 50,
        MaxDailyTrades = 100,
        MinOrderAmount = 0,
        RequireConfirmation = false,
    };

    private static AccountBalanceSummary AllUsdtPortfolio() => new()
    {
        TotalValueUSDT = 1000m,
        Assets = [new AssetBalance { Asset = "USDT", Free = 1000m, Locked = 0m, ValueUSDT = 1000m }]
    };

    private static RiskContext CreateContext(
        bool isFutures = false,
        RiskConfig? config = null,
        AccountBalanceSummary? portfolio = null,
        List<Position>? localPositions = null,
        List<ExchangePosition>? exchangePositions = null)
    {
        var exchange = new Mock<IExchangeClient>();
        exchange.SetupGet(c => c.IsFutures).Returns(isFutures);
        exchange.Setup(c => c.GetAccountInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountInfo
            {
                CanTrade = true,
                Balances =
                [
                    new ExchangeBalance { Asset = "BTC", Free = 100m },
                    new ExchangeBalance { Asset = "USDT", Free = 1000m }
                ]
            });
        exchange.Setup(c => c.GetPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exchangePositions ?? []);

        var portfolioService = new Mock<CryptoPortfolioService>(null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        portfolioService
            .Setup(s => s.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(portfolio ?? AllUsdtPortfolio());

        var data = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        data.Setup(s => s.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config ?? OpenConfig());
        data.Setup(s => s.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats());
        data.Setup(s => s.LoadTradingHaltAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, string.Empty, (DateTime?)null));
        data.Setup(s => s.SaveTradingHaltAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        data.Setup(s => s.SaveAccountSnapshotAsync(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        data.Setup(s => s.GetPeakAccountValueAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        data.Setup(s => s.GetOpenPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(localPositions ?? []);
        data.Setup(s => s.GetStrategyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TradingStrategy?)null);

        var apiHealth = new ApiHealthMonitor();
        var manager = new RiskManager(
            data.Object, portfolioService.Object, exchange.Object,
            new TradingHaltState(data.Object, NullLogger<TradingHaltState>.Instance),
            apiHealth, NullLogger<RiskManager>.Instance, null, null);

        return new RiskContext(manager, data, portfolioService, exchange, apiHealth);
    }

    private static Position LocalPosition(decimal remainingQuantity) => new()
    {
        Symbol = Symbol,
        Side = PositionSide.Long,
        Quantity = remainingQuantity,
        ClosedQuantity = 0m
    };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ApiFailureThreshold_WhenUnhealthy_ShouldRejectOpen_ButExemptExit()
    {
        var config = OpenConfig();
        config.ApiFailureThreshold = 3;
        var ctx = CreateContext(config: config, localPositions: [LocalPosition(1m)]);
        ctx.ApiHealth.RecordFailure();
        ctx.ApiHealth.RecordFailure();
        ctx.ApiHealth.RecordFailure();

        var openResult = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsFalse(openResult.Passed);
        StringAssert.Contains(openResult.Reason ?? string.Empty, "连续失败",
            "账户与持仓数据不可信时继续下单等于盲目承担资金风险（fail-closed）");

        // 退出单豁免：止损失败的代价高于数据不可信，减仓方向本身不增加风险敞口
        var exitResult = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 0.5m, 100m, OrderType.Limit, isExitOrder: true);
        Assert.IsTrue(exitResult.Passed, exitResult.Reason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MinOrderAmount_BelowLimit_ShouldReject()
    {
        var config = OpenConfig();
        config.MinOrderAmount = 10;
        var ctx = CreateContext(config: config);

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 0.05m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "低于最小限额");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MarketOrderSlippageBuffer_ShouldInflateOrderValue_OverPercentCap()
    {
        // 市价单必须按保守的滑点缓冲价格（+1.5%）做风控计算：同参数下限价单放行、市价单超占比拒单
        var config = OpenConfig();
        config.MaxSingleOrderPercent = 0.5m;
        var ctx = CreateContext(config: config);

        var limitResult = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 0.05m, 100m, OrderType.Limit);
        Assert.IsTrue(limitResult.Passed, limitResult.Reason);

        var marketResult = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 0.05m, 100m, OrderType.Market);
        Assert.IsFalse(marketResult.Passed, "市价单滑点缓冲后的金额占比必须参与闸门计算");
        StringAssert.Contains(marketResult.Reason ?? string.Empty, "单笔金额占比");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MaxDailyTrades_Reached_ShouldReject()
    {
        var config = OpenConfig();
        config.MaxDailyTrades = 15;
        var ctx = CreateContext(config: config);
        ctx.Data.Setup(s => s.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats { TradeCount = 15 });

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "今日交易次数 15 已达上限 15");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MaxSingleOrderPercent_Exceeded_ShouldReject()
    {
        var config = OpenConfig();
        config.MaxSingleOrderPercent = 30;
        var ctx = CreateContext(config: config);

        // 5 BTC × 100 = 500 USDT，占总值 1000 的 50% > 30%
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 5m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "单笔金额占比 50.0% 超过限额 30%");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MaxTotalPositionPercent_ProjectedExceeds_ShouldReject()
    {
        var config = OpenConfig();
        config.MaxTotalPositionPercent = 50;
        var ctx = CreateContext(
            config: config,
            portfolio: new AccountBalanceSummary
            {
                TotalValueUSDT = 1000m,
                Assets =
                [
                    new AssetBalance { Asset = "USDT", Free = 600m, Locked = 0m, ValueUSDT = 600m },
                    new AssetBalance { Asset = "BTC", Free = 0.4m, Locked = 0m, ValueUSDT = 400m }
                ]
            });

        // 已持仓 40%，买入 200 USDT（20%）→ 投影 60% > 50%
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 2m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "总仓位占比将达 60.0%，超过限额 50%");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MaxSinglePositionPercent_Exceeded_ShouldReject()
    {
        var config = OpenConfig();
        config.MaxSinglePositionPercent = 40;
        var ctx = CreateContext(
            config: config,
            portfolio: new AccountBalanceSummary
            {
                TotalValueUSDT = 1000m,
                Assets =
                [
                    new AssetBalance { Asset = "USDT", Free = 750m, Locked = 0m, ValueUSDT = 750m },
                    new AssetBalance { Asset = "BTC", Free = 0.25m, Locked = 0m, ValueUSDT = 250m }
                ]
            });

        // BTC 已占 25%，买入 200 USDT → 单标的投影 (250+200)/1000 = 45% > 40%
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 2m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "单标的 BTC 仓位将达 45.0%，超过限额 40%");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task StrategyLevelPositionPercent_StricterThanAccountLevel_ShouldReject()
    {
        // 单标的仓位上限：账户级与策略级配置取更严者，策略级让单条策略可被单独约束
        var config = OpenConfig();
        config.MaxSinglePositionPercent = 60;
        var ctx = CreateContext(
            config: config,
            portfolio: new AccountBalanceSummary
            {
                TotalValueUSDT = 1000m,
                Assets =
                [
                    new AssetBalance { Asset = "USDT", Free = 900m, Locked = 0m, ValueUSDT = 900m },
                    new AssetBalance { Asset = "BTC", Free = 0.1m, Locked = 0m, ValueUSDT = 100m }
                ]
            });
        ctx.Data.Setup(s => s.GetStrategyAsync("strategy-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradingStrategy
            {
                Id = "strategy-1",
                Type = StrategyType.AISignal,
                MaxPositionPercent = 25
            });

        // BTC 已占 10%，买入 200 USDT → 投影 30%：低于账户级 60，但超过策略级 25
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 2m, 100m, OrderType.Limit, strategyId: "strategy-1");

        Assert.IsFalse(result.Passed, "策略级上限与账户级取更严者，必须生效");
        StringAssert.Contains(result.Reason ?? string.Empty, "单标的 BTC 仓位将达 30.0%，超过限额 25%");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotSell_AboveStricterOfLocalAndExchange_ShouldRejectWithBothSources()
    {
        var ctx = CreateContext(localPositions: [LocalPosition(0.5m)]);
        ctx.Exchange.Setup(c => c.GetAccountInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountInfo
            {
                Balances = [new ExchangeBalance { Asset = "BTC", Free = 0.3m, Locked = 0m }]
            });

        // 本地 FIFO 0.5、交易所余额 0.3：只信本地会放行超卖，必须取更严者拒单
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 0.4m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "本地 0.5");
        StringAssert.Contains(result.Reason ?? string.Empty, "交易所 0.3");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotSell_ExchangeBalanceQueryFails_ShouldFailClosed()
    {
        var ctx = CreateContext(localPositions: [LocalPosition(0.5m)]);
        ctx.Exchange.Setup(c => c.GetAccountInfoAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟余额查询失败"));

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 0.4m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "现货卖出校验失败（fail-closed）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task FuturesSell_CloseLongExceedingExchangePosition_ShouldReject()
    {
        var ctx = CreateContext(isFutures: true, exchangePositions:
        [
            new ExchangePosition { Symbol = Symbol, PositionAmt = 1m, EntryPrice = 100m, MarkPrice = 100m }
        ]);

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 2m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "平多数量 2 超过交易所多头持仓 1");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task FuturesSell_PositionQueryFails_ShouldFailClosed()
    {
        var ctx = CreateContext(isFutures: true);
        ctx.Exchange.Setup(c => c.GetPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟持仓查询失败"));

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 0.5m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed,
            "无法确认交易所持仓就放行，平多单可能以 reduceOnly=false 落地变成反向开仓（fail-closed）");
        StringAssert.Contains(result.Reason ?? string.Empty, "合约卖出校验失败");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotBuy_ExceedingUsdtBalance_ShouldReject()
    {
        var config = OpenConfig();
        config.MaxTotalPositionPercent = 0;
        config.MaxSinglePositionPercent = 0;
        var ctx = CreateContext(
            config: config,
            portfolio: new AccountBalanceSummary
            {
                TotalValueUSDT = 1000m,
                Assets =
                [
                    new AssetBalance { Asset = "USDT", Free = 100m, Locked = 0m, ValueUSDT = 100m },
                    new AssetBalance { Asset = "BTC", Free = 0.9m, Locked = 0m, ValueUSDT = 900m }
                ]
            });

        // 买入 200 USDT 超过可用 USDT 余额 100：下单后必被交易所拒绝，事前拦截
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 2m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "买入金额 200.00 USDT 超过可用余额 100.00 USDT");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PortfolioSummaryFetchFails_ShouldFailClosed()
    {
        var ctx = CreateContext();
        ctx.Portfolio
            .Setup(s => s.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new HttpRequestException("模拟账户查询失败"));

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "无法获取账户信息");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SnapshotWriteFailure_ShouldNotBlockTrade()
    {
        var ctx = CreateContext();
        ctx.Data.Setup(s => s.SaveAccountSnapshotAsync(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("模拟快照写入失败"));

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);

        Assert.IsTrue(result.Passed,
            "快照写入只服务回撤熔断的峰值数据，失败不应阻断交易本身");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DrawdownCircuitBreaker_ShouldQueryRolling30DayPeak()
    {
        var config = OpenConfig();
        config.MaxDrawdownPercent = 10;
        var ctx = CreateContext(config: config);
        // 峰值 1050，当前 1000 → 回撤 4.76% < 10%，应放行
        ctx.Data.Setup(s => s.GetPeakAccountValueAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1050m);

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);

        Assert.IsTrue(result.Passed, result.Reason);
        // 回撤窗口必须是 30 天滚动窗口：窗口随时间滑动，历史峰值自动"过期"
        ctx.Data.Verify(s => s.GetPeakAccountValueAsync(
            It.Is<DateTime?>(since => since.HasValue
                && Math.Abs((since.Value - DateTime.UtcNow.AddDays(-30)).TotalMinutes) < 5),
            It.IsAny<CancellationToken>()),
            Times.Once, "回撤峰值必须按最近 30 天滚动窗口查询");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MaxConcurrentPositions_NewSymbolBlocked_ExistingSymbolAllowed()
    {
        var config = OpenConfig();
        config.MaxConcurrentPositions = 2;
        var ctx = CreateContext(
            config: config,
            localPositions:
            [
                new Position { Symbol = "BTCUSDT", Side = PositionSide.Long, Quantity = 1m, ClosedQuantity = 0m },
                new Position { Symbol = "ETHUSDT", Side = PositionSide.Long, Quantity = 2m, ClosedQuantity = 0m }
            ]);

        // 已持有 2 个标的，开第 3 个新标的 = 相关性风险叠仓，必须拒单
        var newSymbol = await ctx.Manager.ValidateOrderAsync(
            "SOLUSDT", OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsFalse(newSymbol.Passed);
        StringAssert.Contains(newSymbol.Reason ?? string.Empty, "已持有 2 个标的");

        // 已有标的加仓不受此限
        var addPosition = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsTrue(addPosition.Passed, addPosition.Reason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RequireLivePromotion_StrategyMissing_ShouldFailClosed()
    {
        var config = OpenConfig();
        config.RequireLivePromotion = true;
        var ctx = CreateContext(config: config);

        // 自动策略 ID 查无对应策略：按未通过准入拒单，宁误拒不放行未经影子验证的真金下单
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit, strategyId: "ai-unknown");

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "策略未通过实盘准入验证");
        StringAssert.Contains(result.Reason ?? string.Empty, "策略信息不可用");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RequireLivePromotion_PerformanceServiceUnavailable_ShouldFailClosed()
    {
        var config = OpenConfig();
        config.RequireLivePromotion = true;
        var ctx = CreateContext(config: config);
        ctx.Data.Setup(s => s.GetStrategyAsync("ai-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradingStrategy { Id = "ai-1", Type = StrategyType.AISignal });

        // 影子绩效查询失败等于没有证据：实盘准入本身是"没证据不许交易"，fail-closed
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit, strategyId: "ai-1");

        Assert.IsFalse(result.Passed);
        StringAssert.Contains(result.Reason ?? string.Empty, "绩效服务不可用");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RequireLivePromotion_ManualOrder_ShouldPass()
    {
        var config = OpenConfig();
        config.RequireLivePromotion = true;
        var ctx = CreateContext(config: config);

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit, strategyId: "manual");

        Assert.IsTrue(result.Passed, result.Reason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RequireLivePromotion_ExitOrder_ShouldBeExempt()
    {
        var config = OpenConfig();
        config.RequireLivePromotion = true;
        var ctx = CreateContext(config: config, localPositions: [LocalPosition(1m)]);

        // 已持仓的止损离场绝不能被准入规则挡住
        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 0.5m, 100m, OrderType.Limit,
            strategyId: "ai-1", isExitOrder: true);

        Assert.IsTrue(result.Passed, result.Reason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EvaluateFundingGuard_NearSettlement_AdverseRate_ShouldRejectPayingSideOnly()
    {
        var config = new RiskConfig { FundingRateGuardMinutes = 30, FundingRateTolerancePercent = 0.03m };
        var premium = new BinancePremiumIndexResponse
        {
            LastFundingRate = 0.0005m, // 0.05%，正费率
            NextFundingTime = DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeMilliseconds()
        };

        // 正费率多头付费：Buy 被推迟，Sell（空头收费）放行
        var buyRejected = RiskManager.EvaluateFundingGuard(
            Symbol, premium, OrderSide.Buy, config, DateTime.UtcNow);
        Assert.IsFalse(buyRejected!.Passed);
        StringAssert.Contains(buyRejected.Reason ?? string.Empty, "距资金结算");
        StringAssert.Contains(buyRejected.Reason ?? string.Empty, "买入（多头）");

        var sellAllowed = RiskManager.EvaluateFundingGuard(
            Symbol, premium, OrderSide.Sell, config, DateTime.UtcNow);
        Assert.IsNull(sellAllowed, "正费率下空头是收费方，不应拦单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EvaluateFundingGuard_NegativeRate_ShouldRejectShortPayingSide()
    {
        var config = new RiskConfig { FundingRateGuardMinutes = 30, FundingRateTolerancePercent = 0.03m };
        var premium = new BinancePremiumIndexResponse
        {
            LastFundingRate = -0.0005m, // 负费率：空头付费、多头收费
            NextFundingTime = DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeMilliseconds()
        };

        var sellRejected = RiskManager.EvaluateFundingGuard(
            Symbol, premium, OrderSide.Sell, config, DateTime.UtcNow);
        Assert.IsFalse(sellRejected!.Passed);
        StringAssert.Contains(sellRejected.Reason ?? string.Empty, "卖出（空头）");

        var buyAllowed = RiskManager.EvaluateFundingGuard(
            Symbol, premium, OrderSide.Buy, config, DateTime.UtcNow);
        Assert.IsNull(buyAllowed, "负费率下多头是收费方，不应拦单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EvaluateFundingGuard_FarSettlement_NoiseRate_OrNoSchedule_ShouldAllow()
    {
        var config = new RiskConfig { FundingRateGuardMinutes = 30, FundingRateTolerancePercent = 0.03m };

        // 距结算超过护栏窗口：不临近结算，放行
        var far = RiskManager.EvaluateFundingGuard(Symbol, new BinancePremiumIndexResponse
        {
            LastFundingRate = 0.0005m,
            NextFundingTime = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds()
        }, OrderSide.Buy, config, DateTime.UtcNow);
        Assert.IsNull(far);

        // |费率| 低于容忍阈值：视为噪声，放行
        var noise = RiskManager.EvaluateFundingGuard(Symbol, new BinancePremiumIndexResponse
        {
            LastFundingRate = 0.0001m, // 0.01% < 0.03%
            NextFundingTime = DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeMilliseconds()
        }, OrderSide.Buy, config, DateTime.UtcNow);
        Assert.IsNull(noise);

        // 交易所未返回下次结算时间：无从判定，放行
        var noSchedule = RiskManager.EvaluateFundingGuard(Symbol, new BinancePremiumIndexResponse
        {
            LastFundingRate = 0.0005m,
            NextFundingTime = 0
        }, OrderSide.Buy, config, DateTime.UtcNow);
        Assert.IsNull(noSchedule);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task FundingGuard_MarketDataServiceUnavailable_ShouldSkipGuard()
    {
        // 资金费率护栏是择时优化（fail-open）：行情服务不可用时不得挡掉开仓
        var config = OpenConfig();
        config.FundingRateGuardMinutes = 30;
        var ctx = CreateContext(isFutures: true, config: config);

        var result = await ctx.Manager.ValidateOrderAsync(
            Symbol, OrderSide.Buy, 1m, 100m, OrderType.Limit);

        Assert.IsTrue(result.Passed, result.Reason);
    }
}

using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 风控网关，所有交易指令必须经过风控检查
/// </summary>
public class RiskManager(
    TradingDataService dataService,
    CryptoPortfolioService portfolioService,
    [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
    TradingHaltState haltState,
    ApiHealthMonitor apiHealthMonitor,
    ILogger<RiskManager> logger,
    BinanceMarketDataService? marketDataService,
    StrategyPerformanceService? performanceService)
{
    private readonly TradingDataService _dataService = dataService;
    private readonly CryptoPortfolioService _portfolioService = portfolioService;
    private readonly IExchangeClient _exchangeClient = exchangeClient;
    private readonly TradingHaltState _haltState = haltState;
    private readonly ApiHealthMonitor _apiHealthMonitor = apiHealthMonitor;
    private readonly BinanceMarketDataService? _marketDataService = marketDataService;
    private readonly StrategyPerformanceService? _performanceService = performanceService;
    private readonly ILogger<RiskManager> _logger = logger;

    /// <summary>
    /// 校验交易是否通过风控检查
    /// </summary>
    /// <param name="isExitOrder">
    /// 本笔交易是否为退出型（止损、追踪止损、网格破网、AI 硬边界等减仓意图）。
    /// 退出单跳过所有"开仓侧"闸门（熔断、仓位上限、敞口上限、金额门槛），
    /// 否则熔断生效期间持仓将失去止损保护；持仓充足性与余额校验仍然保留。
    /// </param>
    /// <remarks>virtual 供单元测试替换（并发卖出锁内复检测试）。</remarks>
    public virtual async Task<RiskCheckResult> ValidateOrderAsync(
        string instrumentSymbol, OrderSide side, decimal quantity, decimal price,
        OrderType orderType = OrderType.Market,
        string? strategyId = null,
        bool isExitOrder = false,
        CancellationToken ct = default)
    {
        // 紧急停止是最高优先级闸门：停机期间经本方法的所有订单一律拒单，退出单也不例外
        // （停机已先暂停全部策略并撤销条件单，此时不存在策略退出单；手动离场走
        // TradeExecutor.ExecuteEmergencyCloseAsync 专用通道，不经过本方法），
        // 必须先于其它校验，避免停机状态下仍做无意义的账户查询
        await _haltState.EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_haltState.IsHalted)
        {
            var reason = string.IsNullOrWhiteSpace(_haltState.HaltReason) ? "未注明原因" : _haltState.HaltReason;
            return RiskCheckResult.Reject(
                $"交易已紧急停止（{reason}），请在交易监控页恢复后重试；如需立即离场请使用「全部平仓」");
        }

        if (isExitOrder)
        {
            _logger.LogInformation(
                "退出单风控豁免: {InstrumentSymbol} {Side} 数量:{Qty}，跳过开仓侧闸门"
                + "（金额门槛/交易次数/连续亏损/API 健康/仓位与敞口上限/回撤与日亏损熔断）",
                instrumentSymbol, side, quantity);
        }

        const decimal MarketOrderSlippageBuffer = 0.015m; // 市价单预留 1.5% 滑点缓冲

        var config = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);

        // 交易所 API 连续异常时拒单：此时账户与持仓数据都不可信，
        // 继续下单等于在盲目状态下承担资金风险（fail-closed）。
        // 退出单豁免：止损失败的代价高于"数据不可信"——减仓方向本身不增加风险敞口
        if (!isExitOrder
            && config.ApiFailureThreshold > 0
            && _apiHealthMonitor.IsUnhealthy(config.ApiFailureThreshold))
        {
            return RiskCheckResult.Reject(
                $"交易所 API 在最近窗口内连续失败（阈值 {config.ApiFailureThreshold} 次），已暂停自动交易");
        }

        // 市价单使用保守的滑点缓冲价格进行风控计算，防止实际成交额超限
        var effectivePrice = orderType == OrderType.Market
            ? price * (1 + MarketOrderSlippageBuffer)
            : price;

        var orderValueUSDT = quantity * effectivePrice;

        // 以下三道闸门只约束开仓侧：退出单即便金额极小（残仓平仓）、
        // 或当日交易次数/连亏已达上限，也必须允许离场
        if (!isExitOrder && orderValueUSDT < config.MinOrderAmount)
            return RiskCheckResult.Reject($"订单金额 {orderValueUSDT:F2} USDT 低于最小限额 {config.MinOrderAmount} USDT");

        var todayStats = await _dataService.GetTodayStatsAsync(ct).ConfigureAwait(false);

        if (!isExitOrder && todayStats.TradeCount >= config.MaxDailyTrades)
            return RiskCheckResult.Reject($"今日交易次数 {todayStats.TradeCount} 已达上限 {config.MaxDailyTrades}");

        // 连续亏损熔断：单日实现亏损笔数连续达到上限时停止新开仓。
        // 只在有亏损记录时判定，未成交/未实现盈亏不参与计数。
        // 退出单豁免：熔断期间拒绝止损会让持仓失去保护，与"停止新开仓"的初衷相反
        if (!isExitOrder
            && config.MaxConsecutiveLosses > 0
            && todayStats.ConsecutiveLosses >= config.MaxConsecutiveLosses)
        {
            return RiskCheckResult.Reject(
                $"今日已连续亏损 {todayStats.ConsecutiveLosses} 笔，达到熔断上限 {config.MaxConsecutiveLosses} 笔，暂停交易");
        }

        AccountBalanceSummary portfolioSummary;
        try
        {
            // 风控路径必须实时估值：3 秒缓存仅供 UI 展示，1 秒级价格 tick 下
            // 连发订单若共用同一份快照会绕过仓位上限
            portfolioSummary = await _portfolioService.GetAccountBalanceSummaryAsync(ct, useCache: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取账户信息用于风控校验失败，拒绝交易（fail-closed）");
            return RiskCheckResult.Reject("无法获取账户信息，风控拒绝交易");
        }

        var totalUSDT = portfolioSummary.TotalValueUSDT;

        if (totalUSDT > 0)
        {
            var orderPercent = orderValueUSDT / totalUSDT * 100;
            if (!isExitOrder && orderPercent > config.MaxSingleOrderPercent)
                return RiskCheckResult.Reject(
                    $"单笔金额占比 {orderPercent:F1}% 超过限额 {config.MaxSingleOrderPercent}%");

            if (!isExitOrder && config.MaxTotalPositionPercent > 0)
            {
                var usdtBalance = CryptoPortfolioService.GetUsdtBalance(portfolioSummary);
                var nonUSDTValue = totalUSDT - usdtBalance;
                var currentPositionPercent = nonUSDTValue / totalUSDT * 100;
                var projectedPercent = side == OrderSide.Buy
                    ? currentPositionPercent + orderPercent
                    : Math.Max(0, currentPositionPercent - orderPercent);
                if (projectedPercent > config.MaxTotalPositionPercent)
                    return RiskCheckResult.Reject(
                        $"总仓位占比将达 {projectedPercent:F1}%，超过限额 {config.MaxTotalPositionPercent}%");
            }

            // 单 symbol 仓位上限（仅买入时检查）：账户级配置与策略级配置取更严者。
            // 策略级上限让"这条策略最多把这个标的买到多大"可被单独约束，
            // 而不是所有策略共享同一个账户级额度互相挤占
            var singlePositionPercent = config.MaxSinglePositionPercent;
            TradingStrategy? strategy = null;
            if (!isExitOrder && !string.IsNullOrEmpty(strategyId))
            {
                strategy = await _dataService.GetStrategyAsync(strategyId, ct).ConfigureAwait(false);
                if (strategy?.MaxPositionPercent is > 0)
                {
                    singlePositionPercent = singlePositionPercent > 0
                        ? Math.Min(singlePositionPercent, strategy.MaxPositionPercent.Value)
                        : strategy.MaxPositionPercent.Value;
                }
            }

            // 实盘准入闸门（可选开启）：未经影子验证的自动策略不允许花真钱。
            // 当前仅 AI 信号策略具备影子绩效可验证（定投/网格暂无影子路径，纳入只会永久拦单），
            // 且只在非退出单上判定——已持仓的止损离场绝不能被准入规则挡住。
            // 自动策略 ID 查无对应策略时按未通过准入拒单（fail-closed，与"杠杆设置
            // 失败即拒开仓"同理，宁可误拒不可放行未经影子验证的真金下单）；
            // 手动单（"manual"）不是闸门对象，正常放行。
            if (!isExitOrder && config.RequireLivePromotion)
            {
                var isAutomatedOrder = !string.IsNullOrEmpty(strategyId)
                    && !string.Equals(strategyId, "manual", StringComparison.OrdinalIgnoreCase);
                var requiresPromotion = strategy is null
                    ? isAutomatedOrder
                    : LivePromotionGate.RequiresPromotion(strategy.Type);

                if (requiresPromotion)
                {
                    var promotion = strategy is null
                        ? new LivePromotionDecision(false, "策略信息不可用，无法核验实盘准入", 0)
                        : await EvaluatePromotionAsync(strategy, ct).ConfigureAwait(false);
                    if (!promotion.Allowed)
                        return RiskCheckResult.Reject($"策略未通过实盘准入验证：{promotion.Reason}");
                }
            }

            if (!isExitOrder && singlePositionPercent > 0 && side == OrderSide.Buy)
            {
                var baseAsset = TryExtractBaseAsset(instrumentSymbol);
                if (!string.IsNullOrEmpty(baseAsset))
                {
                    var symbolValue = portfolioSummary.Assets
                        .Where(a => a.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))
                        .Sum(a => a.ValueUSDT);
                    var symbolPercent = (symbolValue + orderValueUSDT) / totalUSDT * 100;
                    if (symbolPercent > singlePositionPercent)
                        return RiskCheckResult.Reject(
                            $"单标的 {baseAsset} 仓位将达 {symbolPercent:F1}%，超过限额 {singlePositionPercent}%");
                }
            }

            // 卖出订单校验持仓充足性：
            // - 现货：本地 FIFO 持仓追踪不允许超卖，否则会产生负持仓并导致 PnL 计算错误
            // - 合约：做空（卖出开空）无需持仓校验；平多（卖出平多）需检查多头持仓
            if (side == OrderSide.Sell)
            {
                var baseAsset = TryExtractBaseAsset(instrumentSymbol);
                if (string.IsNullOrEmpty(baseAsset))
                {
                    // fail-closed：无法解析基础资产意味着无法校验持仓充足性，必须拒绝而非跳过校验
                    return RiskCheckResult.Reject(
                        $"无法解析交易对 {instrumentSymbol} 的基础资产，卖出持仓校验失败（fail-closed）");
                }

                if (_exchangeClient.IsFutures)
                {
                    // 合约模式：检查交易所实际持仓，仅当持有多头时才校验平仓数量
                    try
                    {
                        var exchangePositions = await _exchangeClient.GetPositionsAsync(instrumentSymbol, ct).ConfigureAwait(false);
                        var longPosition = exchangePositions.FirstOrDefault(p =>
                            string.Equals(p.Symbol, instrumentSymbol, StringComparison.OrdinalIgnoreCase) &&
                            p.PositionAmt > 0);

                        if (longPosition != null && quantity > longPosition.PositionAmt)
                        {
                            return RiskCheckResult.Reject(
                                $"平多数量 {quantity} 超过交易所多头持仓 {longPosition.PositionAmt}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // fail-closed：无法确认交易所持仓就放行，平多单可能在持仓已平后
                        // 以 reduceOnly=false 落地变成反向开仓，与基础资产解析失败的拒单策略保持一致
                        _logger.LogError(ex, "查询交易所持仓用于风控校验失败，拒绝交易（fail-closed）: {Symbol}", instrumentSymbol);
                        return RiskCheckResult.Reject(
                            $"无法查询 {instrumentSymbol} 的交易所持仓，合约卖出校验失败（fail-closed）");
                    }
                }
                else
                {
                    // 现货模式：本地 FIFO 与交易所余额取更严者。
                    // 本地账本只在应用自己成交时更新，外部（币安 App）卖出或条件单成交后会虚高，
                    // 只信本地会让风控放行超出实际可用量的卖单，进而被交易所拒单或产生负持仓。
                    var positions = await _dataService.GetOpenPositionsAsync(instrumentSymbol, ct).ConfigureAwait(false);
                    var localAvailableQty = positions
                        .Where(p => p.Symbol.Equals(instrumentSymbol, StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.RemainingQuantity);

                    var exchangeAvailableQty = await QuerySpotBaseAssetBalanceAsync(instrumentSymbol, ct)
                        .ConfigureAwait(false);
                    if (!exchangeAvailableQty.HasValue)
                    {
                        return RiskCheckResult.Reject(
                            $"无法查询 {instrumentSymbol} 的交易所余额，现货卖出校验失败（fail-closed）");
                    }

                    var availableQty = Math.Min(localAvailableQty, exchangeAvailableQty.Value);

                    if (quantity > availableQty)
                        return RiskCheckResult.Reject(
                            $"卖出数量 {quantity} 超过可用持仓 {availableQty}（本地 {localAvailableQty}，"
                            + $"交易所 {exchangeAvailableQty?.ToString() ?? "未知"}，含部分成交未同步的偏差）");
                }
            }

            // 合约名义总敞口上限：现货"仓位占比"用资产市值衡量，
            // 合约的资产市值只是保证金，真实风险是 Σ|持仓名义|。杠杆下两者可以差 10 倍以上，
            // 因此合约必须单独按名义敞口约束（也是相关性敞口的可落地代理）。
            // 多头开仓（Buy 且当前非空头）与空头开仓（Sell 且当前非多头）均会增加名义敞口；
            // 平仓方向（卖平多 / 买平空）属于减风险操作，不受敞口上限拦截。
            if (!isExitOrder && _exchangeClient.IsFutures && config.MaxFuturesNotionalPercent > 0)
            {
                try
                {
                    var positions = await _exchangeClient.GetPositionsAsync(null, ct).ConfigureAwait(false);
                    var symbolPosition = positions.FirstOrDefault(p =>
                        string.Equals(p.Symbol, instrumentSymbol, StringComparison.OrdinalIgnoreCase));
                    var currentPosAmt = symbolPosition?.PositionAmt ?? 0m;
                    var isClosingLong = side == OrderSide.Sell && currentPosAmt > 0;
                    var isClosingShort = side == OrderSide.Buy && currentPosAmt < 0;

                    if (!isClosingLong && !isClosingShort)
                    {
                        var currentNotional = positions.Sum(p =>
                            Math.Abs(p.PositionAmt) * (p.MarkPrice > 0 ? p.MarkPrice : p.EntryPrice));
                        var projectedPercent = (currentNotional + orderValueUSDT) / totalUSDT * 100;

                        if (projectedPercent > config.MaxFuturesNotionalPercent)
                        {
                            return RiskCheckResult.Reject(
                                $"合约名义总敞口将达 {projectedPercent:F0}%，超过限额 {config.MaxFuturesNotionalPercent:F0}%"
                                + $"（当前 {currentNotional / totalUSDT * 100:F0}%）");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // fail-closed：无法确认现有敞口就不能放行新仓位
                    _logger.LogError(ex, "查询合约持仓用于名义敞口校验失败，拒绝交易（fail-closed）");
                    return RiskCheckResult.Reject("无法查询合约持仓，名义敞口校验失败（fail-closed）");
                }
            }

            // 资金费率护栏（仅合约开仓）：临结算且费率对己方不利时推迟开仓。
            // 放在名义敞口校验之后、余额校验之前——它是最"软"的一道闸门，
            // 前面那些闸门任一不通过就没必要再查行情。
            if (!isExitOrder
                && _exchangeClient.IsFutures
                && config.FundingRateGuardMinutes > 0)
            {
                var fundingRejection = await CheckFundingRateAsync(
                    instrumentSymbol, side, config, ct).ConfigureAwait(false);
                if (fundingRejection != null)
                    return fundingRejection;
            }

            // 持仓标的数上限（仅开仓）：防止多标的同时同向开仓把相关性风险叠成一笔大杠杆。
            // 判断依据是"本单是否会新建一个标的的持仓"，已在持仓中的标的加仓不受此限。
            if (!isExitOrder && config.MaxConcurrentPositions > 0)
            {
                var openPositionSymbols = await GetConcurrentlyHeldSymbolsAsync(instrumentSymbol, ct)
                    .ConfigureAwait(false);
                if (!openPositionSymbols.Contains(instrumentSymbol)
                    && openPositionSymbols.Count >= config.MaxConcurrentPositions)
                {
                    return RiskCheckResult.Reject(
                        $"已持有 {openPositionSymbols.Count} 个标的，达到同时持仓上限 "
                        + $"{config.MaxConcurrentPositions}，请先减少持仓再开新标的");
                }
            }

            // 买入订单校验报价资产（USDT）可用余额：
            // - 现货：本地余额（Free + Locked）必须覆盖订单金额，防止下单后因余额不足被交易所拒绝
            // - 合约：以交易所保证金为准，跳过本地余额校验（杠杆下占用保证金远小于订单名义价值）
            if (side == OrderSide.Buy && !_exchangeClient.IsFutures)
            {
                var availableQuote = CryptoPortfolioService.GetUsdtBalance(portfolioSummary);
                if (orderValueUSDT > availableQuote)
                    return RiskCheckResult.Reject(
                        $"买入金额 {orderValueUSDT:F2} USDT 超过可用余额 {availableQuote:F2} USDT");
            }

            // 最大回撤熔断：峰值取最近 30 天滚动窗口，
            // 窗口随时间滑动，历史峰值自动"过期"，无需手动重置
            if (!isExitOrder && config.MaxDrawdownPercent > 0)
            {
                var peakValue = await _dataService
                    .GetPeakAccountValueAsync(DateTime.UtcNow.AddDays(-30), ct)
                    .ConfigureAwait(false);
                if (peakValue > 0)
                {
                    var drawdownPercent = (peakValue - totalUSDT) / peakValue * 100;
                    if (drawdownPercent >= config.MaxDrawdownPercent)
                        return RiskCheckResult.Reject(
                            $"账户回撤 {drawdownPercent:F1}% 已达熔断阈值 {config.MaxDrawdownPercent}%，停止交易");
                }
            }

            // 日亏损熔断：仅当账户总值达到有意义的最小阈值时才计算百分比，
            // 避免极小余额（如 0.01 USDT）导致百分比爆炸误触发熔断
            const decimal MinMeaningfulTotalUsdt = 10m;
            if (!isExitOrder && totalUSDT >= MinMeaningfulTotalUsdt)
            {
                var dailyLossPercent = Math.Abs(todayStats.TotalPnl) / totalUSDT * 100;
                if (todayStats.TotalPnl < 0 && dailyLossPercent >= config.MaxDailyLossPercent)
                    return RiskCheckResult.Reject(
                        $"今日亏损 {dailyLossPercent:F1}% 已达上限 {config.MaxDailyLossPercent}%");
            }
        }

        if (config.RequireConfirmation && orderValueUSDT >= config.ConfirmationThreshold)
            return RiskCheckResult.RequireConfirmation(
                $"订单金额 {orderValueUSDT:F2} USDT 超过确认阈值 {config.ConfirmationThreshold} USDT，需人工确认");

        // 风控通过后顺带刷新账户快照；另有 OrderStateSyncService 以 2 分钟周期
        // 在监控运行期间持续刷新，共同保证回撤熔断的峰值数据新鲜。
        if (totalUSDT > 0)
        {
            try
            {
                await _dataService.SaveAccountSnapshotAsync(totalUSDT, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 快照写入失败不应阻断交易本身，仅记录
                _logger.LogWarning(ex, "账户快照写入失败，回撤熔断可能滞后");
            }
        }

        _logger.LogInformation("风控检查通过: {InstrumentSymbol} {Side} 数量:{Qty} 价格:{Price}",
            instrumentSymbol, side, quantity, price);
        return RiskCheckResult.Pass();
    }

    /// <summary>
    /// 资金费率护栏：临结算 + 费率对本次开仓方向不利时拒单（推迟开仓）。
    ///
    /// 行情不可用时放行（返回 null）：资金费率只是"择时优化"，不该因为一个非资金安全的
    /// 辅助查询失败而挡掉交易——与持仓/余额那类 fail-closed 校验的性质不同。
    /// 判定规则见 <see cref="EvaluateFundingGuard"/>（纯逻辑，无 IO 便于单测）。
    /// </summary>
    private async Task<RiskCheckResult?> CheckFundingRateAsync(
        string instrumentSymbol, OrderSide side, RiskConfig config, CancellationToken ct)
    {
        if (_marketDataService == null)
            return null;

        try
        {
            var premiumIndex = await _marketDataService
                .GetPremiumIndexAsync(instrumentSymbol, ct).ConfigureAwait(false);
            if (premiumIndex == null)
                return null;

            var rejection = EvaluateFundingGuard(instrumentSymbol, premiumIndex, side, config, DateTime.UtcNow);
            if (rejection != null)
                _logger.LogInformation("资金费率护栏拦单: {Symbol} {Side} {Reason}",
                    instrumentSymbol, side, rejection.Reason);
            return rejection;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "查询资金费率失败，跳过资金费护栏: {Symbol}", instrumentSymbol);
            return null;
        }
    }

    /// <summary>
    /// 资金费率护栏判定（纯逻辑，无 IO 便于单测）。
    ///
    /// 判定规则：
    /// - 距下次结算超过 <see cref="RiskConfig.FundingRateGuardMinutes"/> → 放行（不临近结算）。
    /// - 费率为正时多头付费、空头收费；费率为负时相反。
    ///   本次方向若为付费方且 |费率| 超过 <see cref="RiskConfig.FundingRateTolerancePercent"/> → 拒单（推迟开仓）。
    /// </summary>
    internal static RiskCheckResult? EvaluateFundingGuard(
        string instrumentSymbol, BinancePremiumIndexResponse premiumIndex, OrderSide side,
        RiskConfig config, DateTime utcNow)
    {
        if (premiumIndex.NextFundingTime <= 0)
            return null;

        var nextFundingTime = DateTimeOffset.FromUnixTimeMilliseconds(premiumIndex.NextFundingTime).UtcDateTime;
        var minutesToFunding = (nextFundingTime - utcNow).TotalMinutes;
        if (minutesToFunding < 0 || minutesToFunding > config.FundingRateGuardMinutes)
            return null;

        var fundingRatePercent = premiumIndex.LastFundingRate * 100m;
        if (Math.Abs(fundingRatePercent) < config.FundingRateTolerancePercent)
            return null;

        // 正费率：多头付费；负费率：空头付费
        var sidePays = (fundingRatePercent > 0 && side == OrderSide.Buy)
                       || (fundingRatePercent < 0 && side == OrderSide.Sell);
        if (!sidePays)
            return null;

        return new RiskCheckResult
        {
            Deferred = true,
            Reason = $"{instrumentSymbol} 距资金结算仅 {minutesToFunding:F0} 分钟，"
                + $"当前费率 {fundingRatePercent:F4}% 对本次{SafeSideName(side)}方向不利，已推迟开仓"
        };

        static string SafeSideName(OrderSide value) => value == OrderSide.Buy ? "买入（多头）" : "卖出（空头）";
    }

    /// <summary>
    /// 从交易对符号提取基础资产（如 BTCUSDT → BTC、BTCFDUSD → BTC）。
    /// 复用统一的 <see cref="CryptoSymbolConverter.ExtractBaseCurrency"/>；
    /// 转换器对未匹配到已知计价后缀的输入会原样返回，此时视为解析失败返回 null。
    /// </summary>
    private static string? TryExtractBaseAsset(string instrumentSymbol)
    {
        var baseAsset = CryptoSymbolConverter.ExtractBaseCurrency(instrumentSymbol);
        return string.Equals(baseAsset, instrumentSymbol, StringComparison.OrdinalIgnoreCase)
            ? null
            : baseAsset;
    }

    /// <summary>
    /// 评估策略的实盘准入状态。影子绩效查询失败时 fail-closed（不允许进入实盘）——
    /// 这项校验本身就是"没证据不许交易"，查询失败等于没有证据。
    /// </summary>
    private async Task<LivePromotionDecision> EvaluatePromotionAsync(
        TradingStrategy strategy, CancellationToken ct)
    {
        try
        {
            if (_performanceService == null)
                return new LivePromotionDecision(false, "绩效服务不可用，按未通过准入处理", 0);

            var shadow = await _performanceService.GetShadowAsync(strategy.Id, ct).ConfigureAwait(false);
            var shadowMode = AISignalParams.FromJson(strategy.CustomParams)?.ShadowMode == true;
            return LivePromotionGate.Evaluate(shadow, shadowMode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "查询影子绩效失败，实盘准入按未通过处理: {StrategyId}", strategy.Id);
            return new LivePromotionDecision(false, "无法读取影子绩效，按未通过准入处理", 0);
        }
    }

    /// <summary>
    /// 当前实际持有仓位的标的集合（合约取交易所持仓，现货取本地 FIFO 未平量）。
    /// 查询失败时返回空集合：这项是"额外收紧"约束，不应因辅助查询失败而挡掉所有开仓。
    /// </summary>
    private async Task<HashSet<string>> GetConcurrentlyHeldSymbolsAsync(
        string instrumentSymbol, CancellationToken ct)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (_exchangeClient.IsFutures)
            {
                var exchangePositions = await _exchangeClient.GetPositionsAsync(null, ct).ConfigureAwait(false);
                foreach (var position in exchangePositions.Where(p => p.PositionAmt != 0))
                    symbols.Add(position.Symbol);
            }
            else
            {
                var localPositions = await _dataService.GetOpenPositionsAsync(null, ct).ConfigureAwait(false);
                foreach (var position in localPositions.Where(p => p.RemainingQuantity > 0))
                    symbols.Add(position.Symbol);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "查询持仓标的数量失败，跳过持仓数上限校验: {Symbol}", instrumentSymbol);
            return [];
        }

        // 调用方用 Contains(instrumentSymbol) 判断"是否已在持仓中"，这里补一次规范化
        return symbols;
    }

    /// <summary>
    /// 查询该交易对基础资产在现货账户的可用总量（free + locked）。
    /// 查询失败返回 null，由调用方拒单；只有交易所明确返回余额才参与卖出校验。
    /// </summary>
    private async Task<decimal?> QuerySpotBaseAssetBalanceAsync(string instrumentSymbol, CancellationToken ct)
    {
        var baseAsset = TryExtractBaseAsset(instrumentSymbol);
        if (string.IsNullOrEmpty(baseAsset))
            return null;

        try
        {
            var accountInfo = await _exchangeClient.GetAccountInfoAsync(ct).ConfigureAwait(false);
            var balance = accountInfo.Balances.FirstOrDefault(b =>
                b.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase));
            return balance == null ? 0m : balance.Free + balance.Locked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "查询现货余额失败，卖出校验退回本地持仓: {Symbol}", instrumentSymbol);
            return null;
        }
    }

}

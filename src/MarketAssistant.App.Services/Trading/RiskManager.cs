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
    ILogger<RiskManager> logger)
{
    private readonly TradingDataService _dataService = dataService;
    private readonly CryptoPortfolioService _portfolioService = portfolioService;
    private readonly IExchangeClient _exchangeClient = exchangeClient;
    private readonly TradingHaltState _haltState = haltState;
    private readonly ApiHealthMonitor _apiHealthMonitor = apiHealthMonitor;
    private readonly ILogger<RiskManager> _logger = logger;

    /// <summary>
    /// 校验交易是否通过风控检查
    /// </summary>
    /// <remarks>virtual 供单元测试替换（并发卖出锁内复检测试）。</remarks>
    public virtual async Task<RiskCheckResult> ValidateOrderAsync(
        string instrumentSymbol, OrderSide side, decimal quantity, decimal price,
        OrderType orderType = OrderType.Market,
        string? strategyId = null,
        CancellationToken ct = default)
    {
        // 紧急停止是最高优先级闸门：停机期间经本方法的所有订单一律拒单
        // （紧急全部平仓走 TradeExecutor.ExecuteEmergencyCloseAsync 专用通道，不经过本方法），
        // 必须先于其它校验，避免停机状态下仍做无意义的账户查询
        await _haltState.EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_haltState.IsHalted)
        {
            var reason = string.IsNullOrWhiteSpace(_haltState.HaltReason) ? "未注明原因" : _haltState.HaltReason;
            return RiskCheckResult.Reject($"交易已紧急停止（{reason}），请在交易监控页恢复后重试");
        }

        const decimal MarketOrderSlippageBuffer = 0.015m; // 市价单预留 1.5% 滑点缓冲

        var config = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);

        // 交易所 API 连续异常时拒单：此时账户与持仓数据都不可信，
        // 继续下单等于在盲目状态下承担资金风险（fail-closed）
        if (config.ApiFailureThreshold > 0
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

        if (orderValueUSDT < config.MinOrderAmount)
            return RiskCheckResult.Reject($"订单金额 {orderValueUSDT:F2} USDT 低于最小限额 {config.MinOrderAmount} USDT");

        var todayStats = await _dataService.GetTodayStatsAsync(ct).ConfigureAwait(false);

        if (todayStats.TradeCount >= config.MaxDailyTrades)
            return RiskCheckResult.Reject($"今日交易次数 {todayStats.TradeCount} 已达上限 {config.MaxDailyTrades}");

        // 连续亏损熔断：单日实现亏损笔数连续达到上限时停止新开仓。
        // 只在有亏损记录时判定，未成交/未实现盈亏不参与计数
        if (config.MaxConsecutiveLosses > 0 && todayStats.ConsecutiveLosses >= config.MaxConsecutiveLosses)
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
            if (orderPercent > config.MaxSingleOrderPercent)
                return RiskCheckResult.Reject(
                    $"单笔金额占比 {orderPercent:F1}% 超过限额 {config.MaxSingleOrderPercent}%");

            if (config.MaxTotalPositionPercent > 0)
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
            if (!string.IsNullOrEmpty(strategyId))
            {
                var strategy = await _dataService.GetStrategyAsync(strategyId, ct).ConfigureAwait(false);
                if (strategy?.MaxPositionPercent is > 0)
                {
                    singlePositionPercent = singlePositionPercent > 0
                        ? Math.Min(singlePositionPercent, strategy.MaxPositionPercent.Value)
                        : strategy.MaxPositionPercent.Value;
                }
            }

            if (singlePositionPercent > 0 && side == OrderSide.Buy)
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
                        var longPosition = exchangePositions?.FirstOrDefault(p =>
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
                    // 现货模式：使用本地 FIFO 持仓追踪校验
                    // 注意用剩余未平仓数量（Quantity - ClosedQuantity）而非原始开仓量，
                    // 否则部分平仓后仍按全额校验，会允许超出实际可卖数量的超卖
                    var positions = await _dataService.GetOpenPositionsAsync(instrumentSymbol, ct).ConfigureAwait(false);
                    var availableQty = positions
                        .Where(p => p.Symbol.Equals(instrumentSymbol, StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.RemainingQuantity);
                    if (quantity > availableQty)
                        return RiskCheckResult.Reject(
                            $"卖出数量 {quantity} 超过可用持仓 {availableQty}（含部分成交未同步的偏差）");
                }
            }

            // 合约名义总敞口上限：现货"仓位占比"用资产市值衡量，
            // 合约的资产市值只是保证金，真实风险是 Σ|持仓名义|。杠杆下两者可以差 10 倍以上，
            // 因此合约必须单独按名义敞口约束（也是相关性敞口的可落地代理）。
            // 多头开仓（Buy 且当前非空头）与空头开仓（Sell 且当前非多头）均会增加名义敞口；
            // 平仓方向（卖平多 / 买平空）属于减风险操作，不受敞口上限拦截。
            if (_exchangeClient.IsFutures && config.MaxFuturesNotionalPercent > 0)
            {
                try
                {
                    var positions = await _exchangeClient.GetPositionsAsync(null, ct).ConfigureAwait(false) ?? [];
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
            if (config.MaxDrawdownPercent > 0)
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
            if (totalUSDT >= MinMeaningfulTotalUsdt)
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

}

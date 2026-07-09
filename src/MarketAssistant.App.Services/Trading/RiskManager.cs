using MarketAssistant.DataProviders;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 风控网关，所有交易指令必须经过风控检查
/// </summary>
public class RiskManager
{
    private static readonly string[] QuoteAssets = { "USDT", "USDC", "BUSD", "BTC", "ETH", "BNB" };

    private readonly TradingDataService _dataService;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly IExchangeClient _exchangeClient;
    private readonly ILogger<RiskManager> _logger;

    public RiskManager(
        TradingDataService dataService,
        CryptoPortfolioService portfolioService,
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        ILogger<RiskManager> logger)
    {
        _dataService = dataService;
        _portfolioService = portfolioService;
        _exchangeClient = exchangeClient;
        _logger = logger;
    }

    /// <summary>
    /// 校验交易是否通过风控检查
    /// </summary>
    public async Task<RiskCheckResult> ValidateOrderAsync(
        string instrumentSymbol, OrderSide side, decimal quantity, decimal price,
        OrderType orderType = OrderType.Market,
        CancellationToken ct = default)
    {
        const decimal MarketOrderSlippageBuffer = 0.015m; // 市价单预留 1.5% 滑点缓冲

        var config = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);

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

        AccountBalanceSummary portfolioSummary;
        try
        {
            portfolioSummary = await _portfolioService.GetAccountBalanceSummaryAsync(ct).ConfigureAwait(false);
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

            // 单 symbol 仓位上限（仅买入时检查）
            if (config.MaxSinglePositionPercent > 0 && side == OrderSide.Buy)
            {
                var baseAsset = ExtractBaseAsset(instrumentSymbol);
                if (!string.IsNullOrEmpty(baseAsset))
                {
                    var symbolValue = portfolioSummary.Assets
                        .Where(a => a.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase))
                        .Sum(a => a.ValueUSDT);
                    var symbolPercent = (symbolValue + orderValueUSDT) / totalUSDT * 100;
                    if (symbolPercent > config.MaxSinglePositionPercent)
                        return RiskCheckResult.Reject(
                            $"单标的 {baseAsset} 仓位将达 {symbolPercent:F1}%，超过限额 {config.MaxSinglePositionPercent}%");
                }
            }

            // 卖出订单校验持仓充足性：
            // - 现货：本地 FIFO 持仓追踪不允许超卖，否则会产生负持仓并导致 PnL 计算错误
            // - 合约：做空（卖出开空）无需持仓校验；平多（卖出平多）需检查多头持仓
            if (side == OrderSide.Sell)
            {
                var baseAsset = ExtractBaseAsset(instrumentSymbol);
                if (!string.IsNullOrEmpty(baseAsset))
                {
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
                            // 查询交易所持仓失败时不阻止交易（可能是网络问题），仅记录警告
                            _logger.LogWarning(ex, "查询交易所持仓用于风控校验失败，跳过合约平多校验: {Symbol}", instrumentSymbol);
                        }
                    }
                    else
                    {
                        // 现货模式：使用本地 FIFO 持仓追踪校验
                        var positions = await _dataService.GetOpenPositionsAsync(instrumentSymbol, ct).ConfigureAwait(false);
                        var availableQty = positions
                            .Where(p => p.Symbol.Equals(instrumentSymbol, StringComparison.OrdinalIgnoreCase))
                            .Sum(p => p.Quantity);
                        if (quantity > availableQty)
                            return RiskCheckResult.Reject(
                                $"卖出数量 {quantity} 超过可用持仓 {availableQty}（含部分成交未同步的偏差）");
                    }
                }
            }

            // 最大回撤熔断
            if (config.MaxDrawdownPercent > 0)
            {
                var peakValue = await _dataService.GetPeakAccountValueAsync(ct).ConfigureAwait(false);
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

        // 风控通过后顺带刷新账户快照，供最大回撤熔断使用。
        // 每笔交易必经风控，无需单独的定时器即可保证快照新鲜。
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
    /// 从交易对符号提取基础资产（如 BTCUSDT → BTC）
    /// </summary>
    private static string ExtractBaseAsset(string instrumentSymbol)
    {
        // 常见报价资产后缀
        foreach (var quote in QuoteAssets)
        {
            if (instrumentSymbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
                return instrumentSymbol[..^quote.Length];
        }
        return string.Empty;
    }

}

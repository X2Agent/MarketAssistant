using System.Text.Json;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 策略引擎，管理用户策略并评估触发条件
/// </summary>
public class StrategyEngine
{
    private readonly TradingDataService _dataService;
    private readonly ILogger<StrategyEngine> _logger;

    public StrategyEngine(TradingDataService dataService, ILogger<StrategyEngine> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// 清理指定策略的追踪止损峰值（策略完成或删除时调用）
    /// </summary>
    public async Task ClearPeakPriceAsync(string strategyId, CancellationToken ct = default)
        => await _dataService.UpdateStrategyTrailingPeakAsync(strategyId, null, ct);

    /// <summary>
    /// 评估指定交易标的的所有活跃策略，返回触发的策略列表
    /// </summary>
    public async Task<List<TradingStrategy>> EvaluateStrategiesAsync(
        string symbol, decimal currentPrice, CancellationToken ct = default)
    {
        var activeStrategies = await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active, ct);
        var triggered = new List<TradingStrategy>();

        foreach (var strategy in activeStrategies)
        {
            if (!strategy.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            if (strategy.MaxExecutions.HasValue && strategy.ExecutionCount >= strategy.MaxExecutions.Value)
            {
                await _dataService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, ct);
                continue;
            }

            var (triggeredFlag, effectiveSide, effectiveQty) = await IsTriggeredAsync(strategy, currentPrice, ct);
            if (triggeredFlag)
            {
                strategy.Side = effectiveSide;
                strategy.Quantity = effectiveQty;
                _logger.LogInformation(
                    "策略触发: {StrategyId} {Type} {Symbol} 触发价:{TriggerPrice} 当前价:{CurrentPrice}",
                    strategy.Id, strategy.Type, symbol, strategy.TriggerPrice, currentPrice);

                triggered.Add(strategy);
            }
        }

        return triggered;
    }

    private async Task<(bool Triggered, OrderSide Side, decimal Qty)> IsTriggeredAsync(
        TradingStrategy strategy, decimal currentPrice, CancellationToken ct)
    {
        return strategy.Type switch
        {
            StrategyType.StopLoss => (EvaluateStopLoss(strategy, currentPrice), strategy.Side, strategy.Quantity),
            StrategyType.TakeProfit => (EvaluateTakeProfit(strategy, currentPrice), strategy.Side, strategy.Quantity),
            StrategyType.TrailingStop => (await EvaluateTrailingStopAsync(strategy, currentPrice, ct), strategy.Side, strategy.Quantity),
            StrategyType.AISignal => (EvaluateAISignal(strategy), strategy.Side, strategy.Quantity),
            StrategyType.GridTrading => EvaluateGridTrading(strategy, currentPrice, out var gs, out var gq) ? (true, gs, gq) : (false, strategy.Side, strategy.Quantity),
            StrategyType.DCA => await EvaluateDCAAsync(strategy, currentPrice, ct),
            _ => (false, strategy.Side, strategy.Quantity)
        };
    }

    private static bool EvaluateStopLoss(TradingStrategy strategy, decimal currentPrice)
    {
        // Side 表示触发时要执行的操作方向
        // Sell 侧止损：持有多头仓位，价格跌破触发价时卖出止损
        if (strategy.Side == OrderSide.Sell)
            return currentPrice <= strategy.TriggerPrice;
        // Buy 侧止损：持有空头仓位，价格涨破触发价时买入止损
        return currentPrice >= strategy.TriggerPrice;
    }

    private static bool EvaluateTakeProfit(TradingStrategy strategy, decimal currentPrice)
    {
        // Sell 侧止盈：持有多头仓位，价格涨至触发价时卖出止盈
        if (strategy.Side == OrderSide.Sell)
            return currentPrice >= strategy.TriggerPrice;
        // Buy 侧止盈：等待买入机会，价格跌至触发价时买入
        return currentPrice <= strategy.TriggerPrice;
    }

    private async Task<bool> EvaluateTrailingStopAsync(TradingStrategy strategy, decimal currentPrice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(strategy.CustomParams);
            var root = doc.RootElement;

            if (!root.TryGetProperty("activationPrice", out var activationPriceEl))
                return false;
            var activationPrice = activationPriceEl.GetDecimal();

            if (!root.TryGetProperty("trailingPercent", out var trailingPercentEl))
                return false;
            var trailingPercent = trailingPercentEl.GetDecimal();

            if (strategy.Side == OrderSide.Sell)
            {
                // 未激活且价格未达到激活价：不触发
                if (!strategy.TrailingPeakPrice.HasValue && currentPrice < activationPrice)
                    return false;

                // 追踪最高价（从持久化字段恢复），从峰值回撤 trailingPercent% 时触发卖出
                var peak = Math.Max(strategy.TrailingPeakPrice ?? currentPrice, currentPrice);
                var trailPrice = peak * (1 - trailingPercent / 100);

                // 持久化更新峰值
                if (!strategy.TrailingPeakPrice.HasValue || peak > strategy.TrailingPeakPrice.Value)
                {
                    strategy.TrailingPeakPrice = peak;
                    await _dataService.UpdateStrategyTrailingPeakAsync(strategy.Id, peak, ct);
                }

                return currentPrice <= trailPrice;
            }
            else
            {
                if (!strategy.TrailingPeakPrice.HasValue && currentPrice > activationPrice)
                    return false;

                // 追踪最低价（从持久化字段恢复），从谷值反弹 trailingPercent% 时触发买入
                var trough = Math.Min(strategy.TrailingPeakPrice ?? currentPrice, currentPrice);
                var trailPrice = trough * (1 + trailingPercent / 100);

                if (!strategy.TrailingPeakPrice.HasValue || trough < strategy.TrailingPeakPrice.Value)
                {
                    strategy.TrailingPeakPrice = trough;
                    await _dataService.UpdateStrategyTrailingPeakAsync(strategy.Id, trough, ct);
                }

                return currentPrice >= trailPrice;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 TrailingStop 参数失败: {StrategyId}", strategy.Id);
            return false;
        }
    }

    // 未配置时的安全默认值，防止每个价格 tick 都触发 AI 调用
    private const int DefaultAISignalIntervalSeconds = 60;

    private bool EvaluateAISignal(TradingStrategy strategy)
    {
        var intervalSeconds = DefaultAISignalIntervalSeconds;

        if (!string.IsNullOrEmpty(strategy.CustomParams))
        {
            try
            {
                using var doc = JsonDocument.Parse(strategy.CustomParams);
                var root = doc.RootElement;
                if (root.TryGetProperty("analysisInterval", out var intervalEl))
                    intervalSeconds = intervalEl.GetInt32();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 AISignal 参数失败，使用默认间隔 {DefaultInterval}s: {StrategyId}",
                    DefaultAISignalIntervalSeconds, strategy.Id);
            }
        }

        if (strategy.LastTriggeredAt.HasValue)
        {
            var elapsed = (DateTime.UtcNow - strategy.LastTriggeredAt.Value).TotalSeconds;
            return elapsed >= intervalSeconds;
        }

        return true;
    }

    /// <summary>
    /// 网格交易评估：价格穿越网格线时触发交易。
    /// 网格在 LowerPrice 和 UpperPrice 之间均匀分布。
    /// 价格下穿网格线时买入，上穿时卖出。
    /// </summary>
    private bool EvaluateGridTrading(TradingStrategy strategy, decimal currentPrice,
        out OrderSide effectiveSide, out decimal effectiveQty)
    {
        effectiveSide = strategy.Side;
        effectiveQty = strategy.Quantity;

        if (string.IsNullOrEmpty(strategy.CustomParams))
            return false;

        try
        {
            var gridParams = JsonSerializer.Deserialize<GridTradingParams>(strategy.CustomParams);
            if (gridParams == null || gridParams.GridCount <= 1 || gridParams.UpperPrice <= gridParams.LowerPrice)
                return false;

            if (currentPrice < gridParams.LowerPrice)
            {
                // 价格跌破网格下界：检查破网止损
                if (gridParams.StopLossPrice.HasValue && currentPrice <= gridParams.StopLossPrice.Value)
                {
                    effectiveSide = OrderSide.Sell;
                    effectiveQty = gridParams.QuantityPerGrid * gridParams.GridCount;
                    _logger.LogWarning(
                        "网格破网止损触发: {StrategyId} 价格 {Price} <= 止损位 {StopLoss}，清仓 {Qty}",
                        strategy.Id, currentPrice, gridParams.StopLossPrice, effectiveQty);
                    return true;
                }
                return false;
            }
            if (currentPrice > gridParams.UpperPrice)
            {
                // 价格涨破网格上界：检查破网止盈
                if (gridParams.TakeProfitPrice.HasValue && currentPrice >= gridParams.TakeProfitPrice.Value)
                {
                    effectiveSide = OrderSide.Buy;
                    effectiveQty = gridParams.QuantityPerGrid * gridParams.GridCount;
                    _logger.LogWarning(
                        "网格破网止盈触发: {StrategyId} 价格 {Price} >= 止盈位 {TakeProfit}，清仓 {Qty}",
                        strategy.Id, currentPrice, gridParams.TakeProfitPrice, effectiveQty);
                    return true;
                }
                return false;
            }

            var spacing = gridParams.GridSpacing;
            var currentIndex = (int)((currentPrice - gridParams.LowerPrice) / spacing);
            currentIndex = Math.Clamp(currentIndex, 0, gridParams.GridCount);

            if (gridParams.LastTriggeredIndex < 0)
            {
                gridParams.LastTriggeredIndex = currentIndex;
                strategy.CustomParams = JsonSerializer.Serialize(gridParams);
                return false;
            }

            if (currentIndex == gridParams.LastTriggeredIndex)
                return false;

            effectiveSide = currentIndex < gridParams.LastTriggeredIndex ? OrderSide.Buy : OrderSide.Sell;
            effectiveQty = gridParams.QuantityPerGrid;

            gridParams.LastTriggeredIndex = currentIndex;
            strategy.CustomParams = JsonSerializer.Serialize(gridParams);

            _logger.LogInformation(
                "网格交易触发: {StrategyId} 网格 {Index} → {Side}，价格: {Price}",
                strategy.Id, currentIndex, effectiveSide, currentPrice);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 GridTrading 参数失败: {StrategyId}", strategy.Id);
            return false;
        }
    }

    /// <summary>
    /// DCA（定投）评估：按时间间隔定期买入。
    /// 支持价格上限过滤和低价加倍买入。
    /// </summary>
    private async Task<(bool Triggered, OrderSide Side, decimal Qty)> EvaluateDCAAsync(
        TradingStrategy strategy, decimal currentPrice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return (false, strategy.Side, strategy.Quantity);

        try
        {
            var dcaParams = JsonSerializer.Deserialize<DCAParams>(strategy.CustomParams);
            if (dcaParams == null || dcaParams.AmountPerInterval <= 0)
                return (false, strategy.Side, strategy.Quantity);

            if (strategy.LastTriggeredAt.HasValue)
            {
                var elapsed = (DateTime.UtcNow - strategy.LastTriggeredAt.Value).TotalSeconds;
                if (elapsed < dcaParams.IntervalSeconds)
                    return (false, strategy.Side, strategy.Quantity);
            }

            if (dcaParams.MaxBuyPrice > 0 && currentPrice > dcaParams.MaxBuyPrice)
            {
                _logger.LogDebug("DCA 跳过: {StrategyId} 当前价 {Price} 超过上限 {MaxPrice}",
                    strategy.Id, currentPrice, dcaParams.MaxBuyPrice);
                return (false, strategy.Side, strategy.Quantity);
            }

            var amount = dcaParams.AmountPerInterval;
            if (dcaParams.DoubleBuyBelowPrice > 0 && currentPrice < dcaParams.DoubleBuyBelowPrice)
            {
                // 检查加倍次数上限
                var maxCount = dcaParams.MaxDoubleBuyCount > 0 ? dcaParams.MaxDoubleBuyCount : 3;
                if (dcaParams.DoubleBuyCount >= maxCount)
                {
                    _logger.LogWarning("DCA 加倍次数已达上限 {Max}: {StrategyId}，本次按常规金额买入",
                        maxCount, strategy.Id);
                }
                // 检查加倍冷却期
                else if (dcaParams.LastDoubleBuyAt != null
                         && DateTime.TryParse(dcaParams.LastDoubleBuyAt, out var lastDouble)
                         && (DateTime.UtcNow - lastDouble).TotalSeconds < dcaParams.DoubleBuyCooldownSeconds)
                {
                    _logger.LogDebug("DCA 加倍冷却中: {StrategyId} 距上次加倍 {Elapsed:F0}s < 冷却 {Cooldown}s",
                        strategy.Id, (DateTime.UtcNow - lastDouble).TotalSeconds, dcaParams.DoubleBuyCooldownSeconds);
                }
                else
                {
                    amount *= 2;
                    dcaParams.DoubleBuyCount++;
                    dcaParams.LastDoubleBuyAt = DateTime.UtcNow.ToString("O");
                    strategy.CustomParams = JsonSerializer.Serialize(dcaParams);
                    // 立即持久化加倍计数，防止重启后上限失效
                    await _dataService.UpdateStrategyCustomParamsAsync(strategy.Id, strategy.CustomParams, ct);
                    _logger.LogInformation("DCA 低价加倍: {StrategyId} 价格 {Price} < {Threshold}，加倍买入（第 {Count} 次）",
                        strategy.Id, currentPrice, dcaParams.DoubleBuyBelowPrice, dcaParams.DoubleBuyCount);
                }
            }

            var effectiveSide = OrderSide.Buy;
            var effectiveQty = currentPrice > 0 ? amount / currentPrice : 0;

            return (effectiveQty > 0, effectiveSide, effectiveQty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 DCA 参数失败: {StrategyId}", strategy.Id);
            return (false, strategy.Side, strategy.Quantity);
        }
    }
}

using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, decimal> _peakPrices = new();

    public StrategyEngine(TradingDataService dataService, ILogger<StrategyEngine> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// 清理指定策略的峰值/谷值追踪数据（策略完成或删除时调用）
    /// </summary>
    public void ClearPeakPrice(string strategyId) => _peakPrices.TryRemove(strategyId, out _);

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

            if (IsTriggered(strategy, currentPrice))
            {
                _logger.LogInformation(
                    "策略触发: {StrategyId} {Type} {Symbol} 触发价:{TriggerPrice} 当前价:{CurrentPrice}",
                    strategy.Id, strategy.Type, symbol, strategy.TriggerPrice, currentPrice);
                triggered.Add(strategy);
            }
        }

        return triggered;
    }

    private bool IsTriggered(TradingStrategy strategy, decimal currentPrice)
    {
        return strategy.Type switch
        {
            StrategyType.StopLoss => EvaluateStopLoss(strategy, currentPrice),
            StrategyType.TakeProfit => EvaluateTakeProfit(strategy, currentPrice),
            StrategyType.TrailingStop => EvaluateTrailingStop(strategy, currentPrice),
            StrategyType.AISignal => EvaluateAISignal(strategy),
            StrategyType.GridTrading => EvaluateGridTrading(strategy, currentPrice),
            StrategyType.DCA => EvaluateDCA(strategy, currentPrice),
            _ => false
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

    private bool EvaluateTrailingStop(TradingStrategy strategy, decimal currentPrice)
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
                if (!_peakPrices.ContainsKey(strategy.Id) && currentPrice < activationPrice)
                    return false;

                // 追踪最高价，从峰值回撤 trailingPercent% 时触发卖出
                var peak = _peakPrices.AddOrUpdate(
                    strategy.Id, currentPrice, (_, existing) => Math.Max(existing, currentPrice));
                var trailPrice = peak * (1 - trailingPercent / 100);
                return currentPrice <= trailPrice;
            }
            else
            {
                if (!_peakPrices.ContainsKey(strategy.Id) && currentPrice > activationPrice)
                    return false;

                // 追踪最低价，从谷值反弹 trailingPercent% 时触发买入
                var trough = _peakPrices.AddOrUpdate(
                    strategy.Id, currentPrice, (_, existing) => Math.Min(existing, currentPrice));
                var trailPrice = trough * (1 + trailingPercent / 100);
                return currentPrice >= trailPrice;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 TrailingStop 参数失败: {StrategyId}", strategy.Id);
            return false;
        }
    }

    private bool EvaluateAISignal(TradingStrategy strategy)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return true; // No interval configured, always eligible

        try
        {
            using var doc = JsonDocument.Parse(strategy.CustomParams);
            var root = doc.RootElement;

            if (root.TryGetProperty("analysisInterval", out var intervalEl))
            {
                var intervalSeconds = intervalEl.GetInt32();
                if (strategy.LastTriggeredAt.HasValue)
                {
                    var elapsed = (DateTime.UtcNow - strategy.LastTriggeredAt.Value).TotalSeconds;
                    return elapsed >= intervalSeconds;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 AISignal 参数失败: {StrategyId}", strategy.Id);
            return true;
        }
    }

    /// <summary>
    /// 网格交易评估：价格穿越网格线时触发交易。
    /// 网格在 LowerPrice 和 UpperPrice 之间均匀分布。
    /// 价格下穿网格线时买入，上穿时卖出。
    /// </summary>
    private bool EvaluateGridTrading(TradingStrategy strategy, decimal currentPrice)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return false;

        try
        {
            var gridParams = JsonSerializer.Deserialize<GridTradingParams>(strategy.CustomParams);
            if (gridParams == null || gridParams.GridCount <= 1 || gridParams.UpperPrice <= gridParams.LowerPrice)
                return false;

            // 价格超出网格范围不触发
            if (currentPrice < gridParams.LowerPrice || currentPrice > gridParams.UpperPrice)
                return false;

            // 计算当前价格落在哪一格
            var spacing = gridParams.GridSpacing;
            var currentIndex = (int)((currentPrice - gridParams.LowerPrice) / spacing);
            currentIndex = Math.Clamp(currentIndex, 0, gridParams.GridCount);

            // 与上次触发的网格索引比较，只有穿越才触发
            if (gridParams.LastTriggeredIndex < 0)
            {
                // 首次运行，记录当前位置但不触发
                gridParams.LastTriggeredIndex = currentIndex;
                strategy.CustomParams = JsonSerializer.Serialize(gridParams);
                return false;
            }

            if (currentIndex == gridParams.LastTriggeredIndex)
                return false;

            // 价格穿越了网格线：下穿（index 减小）买入，上穿（index 增大）卖出
            strategy.Side = currentIndex < gridParams.LastTriggeredIndex ? OrderSide.Buy : OrderSide.Sell;
            strategy.Quantity = gridParams.QuantityPerGrid;

            gridParams.LastTriggeredIndex = currentIndex;
            strategy.CustomParams = JsonSerializer.Serialize(gridParams);

            _logger.LogInformation(
                "网格交易触发: {StrategyId} 网格 {Index} → {Side}，价格: {Price}",
                strategy.Id, currentIndex, strategy.Side, currentPrice);
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
    private bool EvaluateDCA(TradingStrategy strategy, decimal currentPrice)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return false;

        try
        {
            var dcaParams = JsonSerializer.Deserialize<DCAParams>(strategy.CustomParams);
            if (dcaParams == null || dcaParams.AmountPerInterval <= 0)
                return false;

            // 检查时间间隔
            if (strategy.LastTriggeredAt.HasValue)
            {
                var elapsed = (DateTime.UtcNow - strategy.LastTriggeredAt.Value).TotalSeconds;
                if (elapsed < dcaParams.IntervalSeconds)
                    return false;
            }

            // 价格上限过滤
            if (dcaParams.MaxBuyPrice > 0 && currentPrice > dcaParams.MaxBuyPrice)
            {
                _logger.LogDebug("DCA 跳过: {StrategyId} 当前价 {Price} 超过上限 {MaxPrice}",
                    strategy.Id, currentPrice, dcaParams.MaxBuyPrice);
                return false;
            }

            // DCA 始终买入方向
            strategy.Side = OrderSide.Buy;

            // 低价加倍买入
            var amount = dcaParams.AmountPerInterval;
            if (dcaParams.DoubleBuyBelowPrice > 0 && currentPrice < dcaParams.DoubleBuyBelowPrice)
            {
                amount *= 2;
                _logger.LogInformation("DCA 低价加倍: {StrategyId} 价格 {Price} < {Threshold}，加倍买入",
                    strategy.Id, currentPrice, dcaParams.DoubleBuyBelowPrice);
            }

            // 将金额转换为数量
            strategy.Quantity = currentPrice > 0 ? amount / currentPrice : 0;

            return strategy.Quantity > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 DCA 参数失败: {StrategyId}", strategy.Id);
            return false;
        }
    }
}

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
    /// 评估指定交易对的所有活跃策略，返回触发的策略列表
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
            StrategyType.GridTrading or StrategyType.DCA =>
                LogUnsupported(strategy.Type, strategy.Id),
            _ => false
        };
    }

    private bool LogUnsupported(StrategyType type, string strategyId)
    {
        _logger.LogWarning("策略类型 {Type} 尚未实现评估逻辑，策略 {StrategyId} 被跳过", type, strategyId);
        return false;
    }

    private static bool EvaluateStopLoss(TradingStrategy strategy, decimal currentPrice)
    {
        // Buy side stop loss: price drops below trigger
        if (strategy.Side == OrderSide.Sell)
            return currentPrice <= strategy.TriggerPrice;
        // Sell side stop loss (short scenario): price rises above trigger
        return currentPrice >= strategy.TriggerPrice;
    }

    private static bool EvaluateTakeProfit(TradingStrategy strategy, decimal currentPrice)
    {
        if (strategy.Side == OrderSide.Sell)
            return currentPrice >= strategy.TriggerPrice;
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
}

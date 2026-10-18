using System.Text.Json;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// AI 信号策略执行器 - 护栏与影子记账部分。
/// 与执行主体拆分为 partial 类：护栏生成（追踪伴随策略 / 固定止损止盈）
/// 与影子退出记账是独立职责，单独成文件便于单测与评审。
/// </summary>
public sealed partial class AISignalStrategyExecutor
{
    private async Task<AISignalExecutionResult> RecordShadowExitAsync(
        TradingStrategy strategy, decimal currentPrice, string reasoning, CancellationToken ct)
    {
        var openEntries = await _shadowTradingService
            .LoadOpenPositionsAsync(strategy.Id, strategy.Symbol, strategy.Side, ct)
            .ConfigureAwait(false);
        var openQuantity = openEntries.Sum(row => row.Quantity - row.ClosedQuantity);

        if (openQuantity <= 0)
        {
            _logger.LogInformation(
                "影子硬性边界触发但无未平持仓，直接完结策略: {StrategyId} {Symbol}", strategy.Id, strategy.Symbol);
            await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, ct)
                .ConfigureAwait(false);
            return AISignalExecutionResult.NoTrade;
        }

        // 多头入场 → 卖出离场；空头入场 → 买入离场
        var exitSide = strategy.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        var pnl = await _shadowTradingService
            .RecordAsync(strategy.Id, strategy.Symbol, exitSide, openQuantity, currentPrice, ct: ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "影子硬性边界平仓结算: {StrategyId} {Symbol} 数量 {Qty} 价 {Price} 盈亏 {Pnl} 原因 {Reason}",
            strategy.Id, strategy.Symbol, openQuantity, currentPrice, pnl, reasoning);

        await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, ct)
            .ConfigureAwait(false);
        return AISignalExecutionResult.NoTrade;
    }

    private async Task<TradingStrategy?> FindTrailingStopCompanionAsync(
        TradingStrategy parentStrategy, CancellationToken ct)
    {
        var activeStrategies = await _strategyService
            .GetStrategiesByStatusAsync(StrategyStatus.Active, ct)
            .ConfigureAwait(false);

        return activeStrategies.FirstOrDefault(s =>
            s.Type == StrategyType.TrailingStop &&
            s.Symbol.Equals(parentStrategy.Symbol, StringComparison.OrdinalIgnoreCase) &&
            s.Side == (parentStrategy.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy) &&
            HasParentStrategyId(s, parentStrategy.Id));
    }

    /// <summary>
    /// 判断伴随策略的 CustomParams 是否显式关联指定父策略（parentStrategyId 字段）。
    /// </summary>
    private static bool HasParentStrategyId(TradingStrategy companion, string parentStrategyId)
    {
        if (string.IsNullOrEmpty(companion.CustomParams))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(companion.CustomParams);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            return doc.RootElement.TryGetProperty("parentStrategyId", out var idProp) &&
                idProp.ValueKind == JsonValueKind.String &&
                string.Equals(idProp.GetString(), parentStrategyId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Truncate(string? text, int maxLength)
        => string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text[..maxLength] + "…";

    /// <summary>
    /// 生成并持久化护栏：
    /// - TrailingStop 出场：创建独立的追踪止损伴随策略（复用引擎现有追踪评估，一次性执行）；
    /// - FixedStop 出场：止损/止盈价写入策略的 StopLossPrice/TakeProfitPrice（硬性边界机制）。
    /// AI 给出的价位仅在与当前价方向关系合理时采用，否则按档案百分比兜底，保证护栏方向永不颠倒。
    /// </summary>
    private async Task ApplyGuardrailsAsync(
        TradingStrategy strategy,
        AISignalParams aiParams,
        OrderSide entrySide,
        decimal currentPrice,
        decimal executedQty,
        AISignalDecision decision,
        CancellationToken ct)
    {
        var isLong = entrySide == OrderSide.Buy;

        if (aiParams.ParsedExitStyle == ExitStyle.TrailingStop)
        {
            var trailingPercent = aiParams.TrailingPercent > 0
                ? aiParams.TrailingPercent
                : ScenarioPresets.GetTrailingPercent(aiParams.ParsedRiskProfile);
            var companion = new TradingStrategy
            {
                Symbol = strategy.Symbol,
                Type = StrategyType.TrailingStop,
                Status = StrategyStatus.Active,
                // 多头入场 → 追踪卖出出场；空头入场 → 追踪买入出场
                Side = isLong ? OrderSide.Sell : OrderSide.Buy,
                TriggerPrice = currentPrice,
                Quantity = executedQty,
                MaxExecutions = 1,
                CustomParams = JsonSerializer.Serialize(new
                {
                    parentStrategyId = strategy.Id,
                    trailingPercent,
                    activationPrice = currentPrice
                })
            };

            // 去重：同 Symbol + TrailingStop + Active 且 CustomParams 关联同一父策略的伴随策略已存在时，
            // 更新其数量/触发价/参数而不是新建，防止多次加仓后堆积大量重复的追踪止损策略
            var existing = await FindTrailingStopCompanionAsync(strategy, ct).ConfigureAwait(false);
            if (existing != null)
            {
                companion.Id = existing.Id;
                companion.ExecutionCount = existing.ExecutionCount;
                companion.TrailingPeakPrice = existing.TrailingPeakPrice;
                await _strategyService.SaveStrategyAsync(companion, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "AI 已更新既有追踪止损伴随策略: {CompanionId} 回调 {Percent}% 激活价 {Activation} 关联 {StrategyId}",
                    companion.Id, trailingPercent, currentPrice, strategy.Id);
                return;
            }

            await _strategyService.SaveStrategyAsync(companion, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "AI 已创建追踪止损伴随策略: {CompanionId} 回调 {Percent}% 激活价 {Activation} 关联 {StrategyId}",
                companion.Id, trailingPercent, currentPrice, strategy.Id);
            return;
        }

        decimal? aiStopLoss = decision.StopLossPrice;
        var stopLossValid = aiStopLoss.HasValue &&
            (isLong ? aiStopLoss.Value < currentPrice : aiStopLoss.Value > currentPrice);
        var stopLossFallback = isLong
            ? currentPrice * (1 - aiParams.StopLossPercent / 100m)
            : currentPrice * (1 + aiParams.StopLossPercent / 100m);
        var stopLoss = Math.Round(stopLossValid ? aiStopLoss!.Value : stopLossFallback, 8);

        decimal? aiTakeProfit = decision.TakeProfitPrice;
        var takeProfitValid = aiTakeProfit.HasValue &&
            (isLong ? aiTakeProfit.Value > currentPrice : aiTakeProfit.Value < currentPrice);
        var takeProfitFallback = isLong
            ? currentPrice * (1 + aiParams.TakeProfitPercent / 100m)
            : currentPrice * (1 - aiParams.TakeProfitPercent / 100m);
        var takeProfit = Math.Round(takeProfitValid ? aiTakeProfit!.Value : takeProfitFallback, 8);

        strategy.StopLossPrice = stopLoss;
        strategy.TakeProfitPrice = takeProfit;
        await _dataService.UpdateStrategyGuardrailsAsync(strategy.Id, stopLoss, takeProfit, ct)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "AI 护栏已附加: {StrategyId} 止损 {StopLoss} 止盈 {TakeProfit}（{Source}）",
            strategy.Id, stopLoss, takeProfit,
            decision.StopLossPrice.HasValue || decision.TakeProfitPrice.HasValue
                ? "AI 决策价" : "风险档案兜底价");
    }

    private static bool TryHandleHardBoundary(
        TradingStrategy strategy,
        decimal currentPrice,
        out string reasoning)
    {
        reasoning = string.Empty;

        if (strategy.ExecutionCount == 0)
            return false;

        if (strategy.StopLossPrice.HasValue)
        {
            var stopTriggered = strategy.Side == OrderSide.Buy
                ? currentPrice <= strategy.StopLossPrice.Value
                : currentPrice >= strategy.StopLossPrice.Value;
            if (stopTriggered)
            {
                reasoning = $"AISignal 硬性止损触发：当前价 {currentPrice} 已达止损位 {strategy.StopLossPrice.Value}，系统强制平仓";
                return true;
            }
        }

        if (strategy.TakeProfitPrice.HasValue)
        {
            var tpTriggered = strategy.Side == OrderSide.Buy
                ? currentPrice >= strategy.TakeProfitPrice.Value
                : currentPrice <= strategy.TakeProfitPrice.Value;
            if (tpTriggered)
            {
                reasoning = $"AISignal 硬性止盈触发：当前价 {currentPrice} 已达止盈位 {strategy.TakeProfitPrice.Value}，系统自动止盈";
                return true;
            }
        }

        return false;
    }
}


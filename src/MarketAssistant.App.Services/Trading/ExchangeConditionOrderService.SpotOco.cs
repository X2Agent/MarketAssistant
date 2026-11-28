using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易所侧条件单管理器 - 现货 OCO 部分。
/// 与对账主体拆分为 partial 文件：现货双腿的 OCO 保障（补挂/漂移重建/持仓门控数量封顶）
/// 是独立于单腿对账的职责，单独成文件便于评审（文件规模护栏）。
/// </summary>
public sealed partial class ExchangeConditionOrderService
{
    /// <summary>
    /// 该策略自身的未平持仓（按 StrategyId 精确归属，不按 symbol 模糊匹配——
    /// 同标的可能有多条策略，别的策略的仓位不属于本策略的护栏范围）。
    /// 返回余量与方向（多头行为准，与 AI 执行器的方向解析同口径）。
    /// </summary>
    private async Task<(decimal Remaining, bool IsLong)> GetOwnOpenPositionAsync(
        TradingStrategy strategy, CancellationToken ct)
    {
        var openPositions = await _dataService.GetOpenPositionsAsync(strategy.Symbol, ct).ConfigureAwait(false);
        var own = openPositions
            .Where(p => string.Equals(p.StrategyId, strategy.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var longQty = own.Where(p => p.Side == PositionSide.Long).Sum(p => p.RemainingQuantity);
        var shortQty = own.Where(p => p.Side == PositionSide.Short).Sum(p => p.RemainingQuantity);
        return (longQty + shortQty, longQty >= shortQty);
    }

    /// <summary>
    /// 现货 AI 双腿的 OCO 保障：两腿都在途且指纹一致 → 无操作；任一腿缺失或参数漂移 →
    /// 撤掉在途成员单（撤销任一成员即撤销整个 orderList）后重挂。
    /// </summary>
    private async Task EnsureSpotOcoAsync(
        IExchangeClient exchangeClient,
        TradingStrategy strategy,
        decimal quantity,
        List<ExchangeOrderResult> managedOrders,
        CancellationToken ct)
    {
        var stopId = TradeExecutor.BuildConditionOrderClientOrderId(strategy.Id, ConditionOrderLeg.Stop);
        var takeProfitId = TradeExecutor.BuildConditionOrderClientOrderId(strategy.Id, ConditionOrderLeg.TakeProfit);
        var existingStop = managedOrders.FirstOrDefault(o =>
            string.Equals(o.ClientOrderId, stopId, StringComparison.Ordinal));
        var existingTakeProfit = managedOrders.FirstOrDefault(o =>
            string.Equals(o.ClientOrderId, takeProfitId, StringComparison.Ordinal));

        var stopFingerprint = BuildFingerprint(strategy, ConditionOrderLeg.Stop, quantity);
        var takeProfitFingerprint = BuildFingerprint(strategy, ConditionOrderLeg.TakeProfit, quantity);
        if (existingStop != null && existingTakeProfit != null)
        {
            _liveOrderIds[(strategy.Id, ConditionOrderLeg.Stop)] = existingStop.OrderId;
            _liveOrderIds[(strategy.Id, ConditionOrderLeg.TakeProfit)] = existingTakeProfit.OrderId;

            // 指纹一致（腿别|方向|数量|触发价|回调基点）说明在途 OCO 就是当前护栏期望的形态；
            // 漂移（用户改价/AI 更新护栏）走下方撤旧重挂
            if (existingStop.RequestedQty == existingTakeProfit.RequestedQty
                && HasMatchingFingerprint(strategy.ConditionOrderFingerprint, FormattableString.Invariant($"{stopFingerprint}|{existingStop.RequestedQty:G29}"))
                && HasMatchingFingerprint(strategy.ConditionOrderFingerprint, FormattableString.Invariant($"{takeProfitFingerprint}|{existingTakeProfit.RequestedQty:G29}")))
                return;

        }

        // 只有一腿在途（异常中间态）：撤掉该成员单让整个 orderList 消失，随后整单重挂
        if ((existingStop ?? existingTakeProfit) is { } orphan)
        {
            try
            {
                await exchangeClient.CancelOrderAsync(orphan.Symbol, orphan.OrderId, ct).ConfigureAwait(false);
                _logger.LogInformation("现货 OCO 只有一腿在途，已撤销待整单重挂: {Symbol} {OrderId} {ClientOrderId}",
                    orphan.Symbol, orphan.OrderId, orphan.ClientOrderId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "撤销残缺 OCO 成员单失败，下个对账周期重试: {OrderId}", orphan.OrderId);
                return;
            }
        }

        // OCO 数量取策略护栏数量与实际持仓余量的较小者：部分成交/加仓会让两者漂移，
        // 超量卖出在现货会卖出用户其他来源的资产
        ClearLiveOrder(strategy.Id);
        if (quantity <= 0) return;

        var result = await _tradeExecutor.PlaceSpotProtectiveOcoAsync(strategy, quantity, ct)
            .ConfigureAwait(false);
        if (result.Success && result.StopRecord != null && result.TakeProfitRecord != null)
        {
            _liveOrderIds[(strategy.Id, ConditionOrderLeg.Stop)] = result.StopRecord.ExchangeOrderId.ToString();
            _liveOrderIds[(strategy.Id, ConditionOrderLeg.TakeProfit)] = result.TakeProfitRecord.ExchangeOrderId.ToString();

            // 双腿指纹一次写入。指纹表达"策略参数的期望形态"（与单腿路径同语义），
            // 用于检测护栏价/数量变化；实挂数量可能因持仓封顶小于 strategy.Quantity，
            // 不会写进指纹，避免封顶场景被误判为漂移而反复撤挂
            var updatedFingerprint = FormattableString.Invariant($"{stopFingerprint}|{result.StopRecord.RequestedQty:G29};{takeProfitFingerprint}|{result.TakeProfitRecord.RequestedQty:G29}");
            strategy.ConditionOrderFingerprint = updatedFingerprint;
            await _dataService
                .UpdateStrategyConditionOrderFingerprintAsync(strategy.Id, updatedFingerprint, ct)
                .ConfigureAwait(false);

            _logger.LogInformation("现货 AI 双腿保护已挂 OCO: {StrategyId} {Symbol} 数量:{Qty}", strategy.Id, strategy.Symbol, quantity);
        }
        else
        {
            _liveOrderIds.TryRemove((strategy.Id, ConditionOrderLeg.Stop), out _);
            _liveOrderIds.TryRemove((strategy.Id, ConditionOrderLeg.TakeProfit), out _);
            _logger.LogWarning("挂现货 OCO 保护单失败: {StrategyId} {Error}", strategy.Id, result.Error);
        }
    }
}

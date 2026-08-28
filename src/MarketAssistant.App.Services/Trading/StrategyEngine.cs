using System.Globalization;
using System.Text.Json;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 策略引擎，管理用户策略并评估触发条件。
/// 合约模式下支持将止损/止盈/追踪止损提交为交易所原生条件单，
/// 由交易所服务端监控触发，无需客户端持续轮询价格。
/// </summary>
public class StrategyEngine
{
    private readonly TradingDataService _dataService;
    private readonly TradingStrategyService _strategyService;
    private readonly RoutingExchangeClient _exchangeClient;
    private readonly TradingEnvironmentService _environmentService;
    private readonly ILogger<StrategyEngine> _logger;

    public StrategyEngine(
        TradingDataService dataService,
        TradingStrategyService strategyService,
        RoutingExchangeClient exchangeClient,
        TradingEnvironmentService environmentService,
        ILogger<StrategyEngine> logger)
    {
        _dataService = dataService;
        _strategyService = strategyService;
        _exchangeClient = exchangeClient;
        _environmentService = environmentService;
        _logger = logger;
    }

    /// <summary>
    /// 清理指定策略的追踪止损峰值（策略完成或删除时调用）
    /// </summary>
    public async Task ClearPeakPriceAsync(string strategyId, CancellationToken ct = default)
        => await _dataService.UpdateStrategyTrailingPeakAsync(strategyId, null, ct);

    /// <summary>
    /// 评估指定交易标的的所有活跃策略，返回触发的策略列表。
    /// 注意：此方法会修改返回列表中策略对象的 <see cref="TradingStrategy.Side"/> 和
    /// <see cref="TradingStrategy.Quantity"/> 字段（用于反映触发时的有效方向和数量，
    /// 如网格交易、DCA 等动态计算值），调用方依赖这些副作用将策略传递给交易执行器。
    /// </summary>
    public async Task<List<TradingStrategy>> EvaluateAndUpdateStrategiesAsync(
        string symbol, decimal currentPrice, CancellationToken ct = default)
    {
        var activeStrategies = await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active, ct);
        var triggered = new List<TradingStrategy>();

        foreach (var strategy in activeStrategies)
        {
            if (!strategy.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            if (strategy.MaxExecutions.HasValue && strategy.ExecutionCount >= strategy.MaxExecutions.Value)
            {
                await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Completed, ct);
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
            StrategyType.TrailingStop => (await EvaluateAndUpdateTrailingStopAsync(strategy, currentPrice, ct), strategy.Side, strategy.Quantity),
            StrategyType.AISignal => (await EvaluateAndUpdateAISignalAsync(strategy, ct).ConfigureAwait(false), strategy.Side, strategy.Quantity),
            StrategyType.GridTrading => await EvaluateAndUpdateGridTradingAsync(strategy, currentPrice, ct).ConfigureAwait(false),
            StrategyType.DCA => await EvaluateDCAAsync(strategy, currentPrice, ct),
            _ => (false, strategy.Side, strategy.Quantity)
        };
    }

    /// <summary>
    /// 止损触发评估。
    /// 注意 Side 在此处表示"持仓方向"而非执行动作：Sell 侧 = 持有多头（跌破触发价卖出止损），
    /// Buy 侧 = 持有空头（涨破触发价买入止损）。该语义与 <see cref="EvaluateTakeProfit"/> 中的
    /// Buy 侧（跌至触发价买入建仓）不同，两者是刻意区分的设计。
    /// </summary>
    private static bool EvaluateStopLoss(TradingStrategy strategy, decimal currentPrice)
    {
        // Side 表示触发时要执行的操作方向
        // Sell 侧止损：持有多头仓位，价格跌破触发价时卖出止损
        if (strategy.Side == OrderSide.Sell)
            return currentPrice <= strategy.TriggerPrice;
        // Buy 侧止损：持有空头仓位，价格涨破触发价时买入止损
        return currentPrice >= strategy.TriggerPrice;
    }

    /// <summary>
    /// 止盈触发评估。
    /// Sell 侧 = 持有多头，涨至触发价卖出止盈（真止盈）；
    /// Buy 侧 = 尚未建仓，跌至触发价买入，语义上等价于"限价买入"（并非止盈，为历史命名保留）。
    /// </summary>
    private static bool EvaluateTakeProfit(TradingStrategy strategy, decimal currentPrice)
    {
        // Sell 侧止盈：持有多头仓位，价格涨至触发价时卖出止盈
        if (strategy.Side == OrderSide.Sell)
            return currentPrice >= strategy.TriggerPrice;
        // Buy 侧止盈：等待买入机会，价格跌至触发价时买入
        return currentPrice <= strategy.TriggerPrice;
    }

    /// <summary>
    /// 评估追踪止损触发条件。
    /// 注意：此方法会修改入参 <paramref name="strategy"/> 的 <see cref="TradingStrategy.TrailingPeakPrice"/>
    /// 字段以持久化追踪峰值/谷值状态，并同步写入数据存储，调用方依赖此副作用保持内存与持久化状态一致。
    /// 参数回退策略：trailingPercent 缺失时按策略风险档案（CustomParams.riskProfile）取预设回调比例，
    /// activationPrice 缺失时回退到策略触发价；确保安全护栏永不静默失效。
    /// </summary>
    private async Task<bool> EvaluateAndUpdateTrailingStopAsync(TradingStrategy strategy, decimal currentPrice, CancellationToken ct)
    {
        try
        {
            decimal trailingPercent = 0;
            decimal activationPrice = 0;

            if (!string.IsNullOrEmpty(strategy.CustomParams))
            {
                using var doc = JsonDocument.Parse(strategy.CustomParams);
                var root = doc.RootElement;

                if (root.TryGetProperty("trailingPercent", out var trailingPercentEl)
                    && trailingPercentEl.TryGetDecimal(out var parsedPercent))
                    trailingPercent = parsedPercent;

                if (root.TryGetProperty("activationPrice", out var activationPriceEl)
                    && activationPriceEl.TryGetDecimal(out var parsedActivation))
                    activationPrice = parsedActivation;
            }

            if (trailingPercent <= 0)
            {
                var profile = ResolveRiskProfile(strategy);
                trailingPercent = ScenarioPresets.GetTrailingPercent(profile);
                _logger.LogWarning(
                    "追踪止损策略 {StrategyId} 缺少 trailingPercent，按风险档案 {Profile} 回退为 {Percent}%",
                    strategy.Id, profile.GetDisplayName(), trailingPercent);
            }

            // 未配置激活价时回退到策略触发价；两者皆无则立即激活（护栏优先于精度）
            if (activationPrice <= 0 && strategy.TriggerPrice > 0)
                activationPrice = strategy.TriggerPrice;

            if (strategy.Side == OrderSide.Sell)
            {
                // 未激活且价格未达到激活价：不触发（activationPrice 为 0 表示立即激活）
                if (!strategy.TrailingPeakPrice.HasValue && activationPrice > 0 && currentPrice < activationPrice)
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
                if (!strategy.TrailingPeakPrice.HasValue && activationPrice > 0 && currentPrice > activationPrice)
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

    /// <summary>
    /// 解析策略的风险档案：优先读取 CustomParams.riskProfile，缺失时回退稳健档。
    /// </summary>
    private static RiskProfile ResolveRiskProfile(TradingStrategy strategy)
    {
        var aiParams = AISignalParams.FromJson(strategy.CustomParams);
        return aiParams?.ParsedRiskProfile ?? RiskProfile.Balanced;
    }

    // 未配置时的安全默认值，防止每个价格 tick 都触发 AI 调用
    private const int DefaultAISignalIntervalSeconds = 60;

    /// <summary>
    /// AI 信号策略的评估节流：满足间隔条件时触发，并在触发时立即持久化评估时间，
    /// 保证 Agent 决定 HOLD 或被风控拒绝等未成交场景同样进入冷却期，
    /// 避免无成交时每个价格 tick 都重复调用 LLM（高成本）。
    /// 注意：会修改入参 <paramref name="strategy"/> 的 <see cref="TradingStrategy.LastTriggeredAt"/> 字段。
    /// </summary>
    private async Task<bool> EvaluateAndUpdateAISignalAsync(
        TradingStrategy strategy, CancellationToken ct)
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
            if (elapsed < intervalSeconds)
                return false;
        }

        // 触发即记入冷却期：无论后续 Agent 是否实际成交，本次评估都消耗一次节流窗口
        strategy.LastTriggeredAt = DateTime.UtcNow;
        await _dataService.UpdateStrategyLastTriggeredAtAsync(strategy.Id, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 网格交易评估：价格穿越网格线时触发交易。
    /// 网格在 LowerPrice 和 UpperPrice 之间均匀分布。
    /// 价格下穿网格线时买入，上穿时卖出。
    /// 注意：此方法会修改入参 <paramref name="strategy"/> 的 <see cref="TradingStrategy.CustomParams"/>
    /// 字段以更新网格的 LastTriggeredIndex 状态，并将状态持久化到数据存储；
    /// 首次评估的基准索引同样立即落库，避免应用重启后基准丢失导致重复触发。
    /// </summary>
    private async Task<(bool Triggered, OrderSide Side, decimal Qty)> EvaluateAndUpdateGridTradingAsync(
        TradingStrategy strategy, decimal currentPrice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(strategy.CustomParams))
            return (false, strategy.Side, strategy.Quantity);

        try
        {
            var gridParams = JsonSerializer.Deserialize<GridTradingParams>(strategy.CustomParams);
            if (gridParams == null || gridParams.GridCount <= 1 || gridParams.UpperPrice <= gridParams.LowerPrice)
                return (false, strategy.Side, strategy.Quantity);

            if (currentPrice < gridParams.LowerPrice)
            {
                // 价格跌破网格下界：检查破网止损
                if (gridParams.StopLossPrice.HasValue && currentPrice <= gridParams.StopLossPrice.Value)
                {
                    var stopQty = gridParams.QuantityPerGrid * gridParams.GridCount;
                    _logger.LogWarning(
                        "网格破网止损触发: {StrategyId} 价格 {Price} <= 止损位 {StopLoss}，清仓 {Qty}",
                        strategy.Id, currentPrice, gridParams.StopLossPrice, stopQty);
                    return (true, OrderSide.Sell, stopQty);
                }
                return (false, strategy.Side, strategy.Quantity);
            }
            if (currentPrice > gridParams.UpperPrice)
            {
                // 价格涨破网格上界：检查破网止盈。网格在上涨中逐线卖出，突破上界时应卖出剩余库存清仓，
                // 与破网止损方向对称；若反向买入会在高点开出全网格量多头。
                if (gridParams.TakeProfitPrice.HasValue && currentPrice >= gridParams.TakeProfitPrice.Value)
                {
                    var takeQty = gridParams.QuantityPerGrid * gridParams.GridCount;
                    _logger.LogWarning(
                        "网格破网止盈触发: {StrategyId} 价格 {Price} >= 止盈位 {TakeProfit}，清仓 {Qty}",
                        strategy.Id, currentPrice, gridParams.TakeProfitPrice, takeQty);
                    return (true, OrderSide.Sell, takeQty);
                }
                return (false, strategy.Side, strategy.Quantity);
            }

            var spacing = gridParams.GridSpacing;
            var currentIndex = (int)((currentPrice - gridParams.LowerPrice) / spacing);
            currentIndex = Math.Clamp(currentIndex, 0, gridParams.GridCount);

            if (gridParams.LastTriggeredIndex < 0)
            {
                // 首次评估：仅记录基准网格线并立即落库，不触发交易
                gridParams.LastTriggeredIndex = currentIndex;
                strategy.CustomParams = JsonSerializer.Serialize(gridParams);
                await _dataService.UpdateStrategyCustomParamsAsync(strategy.Id, strategy.CustomParams, ct);
                _logger.LogInformation(
                    "网格基准初始化: {StrategyId} 基准网格 {Index}，价格: {Price}",
                    strategy.Id, currentIndex, currentPrice);
                return (false, strategy.Side, strategy.Quantity);
            }

            if (currentIndex == gridParams.LastTriggeredIndex)
                return (false, strategy.Side, strategy.Quantity);

            var effectiveSide = currentIndex < gridParams.LastTriggeredIndex ? OrderSide.Buy : OrderSide.Sell;

            gridParams.LastTriggeredIndex = currentIndex;
            strategy.CustomParams = JsonSerializer.Serialize(gridParams);

            _logger.LogInformation(
                "网格交易触发: {StrategyId} 网格 {Index} → {Side}，价格: {Price}",
                strategy.Id, currentIndex, effectiveSide, currentPrice);
            return (true, effectiveSide, gridParams.QuantityPerGrid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 GridTrading 参数失败: {StrategyId}", strategy.Id);
            return (false, strategy.Side, strategy.Quantity);
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

            // 出场优先：每 tick 评估止盈/止损，不受定投间隔节流限制（护栏必须实时生效）
            var exitTriggered = await EvaluateDCAExitAsync(strategy, dcaParams, currentPrice, ct)
                .ConfigureAwait(false);
            if (exitTriggered.HasValue)
                return exitTriggered.Value;

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
                         && DateTime.TryParse(dcaParams.LastDoubleBuyAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastDouble)
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

    /// <summary>
    /// DCA 出场评估：基于 FIFO 持仓均价判断止盈/止损。
    /// 止盈：均价上涨达 TakeProfitPercent 时全部卖出获利了结，定投继续（从零重新积累）。
    /// 止损：均价下跌达 StopLossPercent 时按 StopLossSellOut 决定清仓卖出或仅暂停策略（保守默认）。
    /// 返回 null 表示未触发任何出场条件，继续走买入评估。
    /// </summary>
    private async Task<(bool Triggered, OrderSide Side, decimal Qty)?> EvaluateDCAExitAsync(
        TradingStrategy strategy, DCAParams dcaParams, decimal currentPrice, CancellationToken ct)
    {
        if (dcaParams.TakeProfitPercent <= 0 && dcaParams.StopLossPercent <= 0)
            return null;

        var positions = await _dataService.GetOpenPositionsAsync(strategy.Symbol, ct).ConfigureAwait(false);
        var totalQty = positions.Sum(p => p.Quantity - p.ClosedQuantity);
        if (totalQty <= 0)
            return null;

        var avgEntry = await _dataService.GetOpenPositionAvgEntryPriceAsync(strategy.Symbol, ct)
            .ConfigureAwait(false);
        if (avgEntry <= 0)
            return null;

        // 止盈：达到止盈线全部卖出
        if (dcaParams.TakeProfitPercent > 0 && currentPrice >= avgEntry * (1 + dcaParams.TakeProfitPercent / 100m))
        {
            _logger.LogInformation(
                "DCA 止盈触发: {StrategyId} 当前价 {Price} >= 均价 {AvgEntry} × (1 + {TakeProfit}%)，清仓 {Qty}",
                strategy.Id, currentPrice, avgEntry, dcaParams.TakeProfitPercent, totalQty);
            return (true, OrderSide.Sell, totalQty);
        }

        // 止损：达到止损线按配置卖出清仓或暂停策略
        if (dcaParams.StopLossPercent > 0 && currentPrice <= avgEntry * (1 - dcaParams.StopLossPercent / 100m))
        {
            if (dcaParams.StopLossSellOut)
            {
                _logger.LogWarning(
                    "DCA 止损清仓触发: {StrategyId} 当前价 {Price} <= 均价 {AvgEntry} × (1 - {StopLoss}%)，清仓 {Qty}",
                    strategy.Id, currentPrice, avgEntry, dcaParams.StopLossPercent, totalQty);
                return (true, OrderSide.Sell, totalQty);
            }

            // 保守动作：暂停定投保留持仓，等待人工决策
            _logger.LogWarning(
                "DCA 止损暂停触发: {StrategyId} 当前价 {Price} <= 均价 {AvgEntry} × (1 - {StopLoss}%)，暂停定投（保留持仓）",
                strategy.Id, currentPrice, avgEntry, dcaParams.StopLossPercent);
            await _strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused, ct)
                .ConfigureAwait(false);
            return (false, strategy.Side, strategy.Quantity);
        }

        return null;
    }

    /// <summary>
    /// 当前交易模式是否支持原生条件单（仅合约模式支持）。
    /// </summary>
    public bool IsNativeConditionalOrderSupported => _exchangeClient.IsFutures;

    /// <summary>
    /// 尝试将止损/止盈/追踪止损策略提交为交易所原生条件单。
    /// 仅合约模式支持；现货模式返回 null，调用方应回退到客户端轮询评估。
    /// </summary>
    /// <returns>交易所返回的订单 ID；不支持或失败时返回 null</returns>
    public async Task<string?> TryPlaceNativeConditionalOrderAsync(
        TradingStrategy strategy, CancellationToken ct = default)
    {
        if (!_exchangeClient.IsFutures)
            return null;

        try
        {
            var orderType = strategy.Type switch
            {
                StrategyType.StopLoss => OrderType.StopMarket,
                StrategyType.TakeProfit => OrderType.TakeProfitMarket,
                StrategyType.TrailingStop => OrderType.TrailingStopMarket,
                _ => (OrderType?)null
            };

            if (!orderType.HasValue)
                return null;

            // 条件单均以 reduceOnly=true 提交，确保只平仓不开新仓
            decimal? stopPrice = null;
            int? trailingDelta = null;

            if (strategy.Type == StrategyType.TrailingStop)
            {
                // 从 CustomParams 解析回调比例（百分比转基点：1% = 100）
                if (!string.IsNullOrEmpty(strategy.CustomParams))
                {
                    using var doc = JsonDocument.Parse(strategy.CustomParams);
                    if (doc.RootElement.TryGetProperty("trailingPercent", out var tpEl))
                    {
                        var percent = tpEl.GetDecimal();
                        trailingDelta = (int)(percent * 100);
                    }
                }
                if (!trailingDelta.HasValue || trailingDelta.Value <= 0)
                {
                    _logger.LogWarning("追踪止损策略 {StrategyId} 缺少 trailingPercent 参数，无法提交原生条件单", strategy.Id);
                    return null;
                }
            }
            else
            {
                stopPrice = strategy.TriggerPrice;
            }

            var result = await _exchangeClient.PlaceOrderAsync(
                strategy.Symbol,
                strategy.Side,
                orderType.Value,
                strategy.Quantity,
                stopPrice: stopPrice,
                reduceOnly: true,
                trailingDelta: trailingDelta,
                ct: ct);

            _logger.LogInformation(
                "策略 {StrategyId} 已提交为原生条件单：{Type} {Side} {Symbol} 订单ID={OrderId}",
                strategy.Id, orderType.Value, strategy.Side, strategy.Symbol, result.OrderId);

            return result.OrderId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "策略 {StrategyId} 提交原生条件单失败，将回退到客户端评估", strategy.Id);
            return null;
        }
    }

    /// <summary>
    /// 取消交易所上的原生条件单（策略完成或删除时调用）。
    /// </summary>
    public async Task<bool> TryCancelNativeConditionalOrderAsync(
        string symbol, string orderId, CancellationToken ct = default)
    {
        if (!_exchangeClient.IsFutures || string.IsNullOrEmpty(orderId))
            return false;

        try
        {
            await _exchangeClient.CancelOrderAsync(symbol, orderId, ct);
            _logger.LogInformation("已取消原生条件单：{Symbol} 订单ID={OrderId}", symbol, orderId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "取消原生条件单失败：{Symbol} 订单ID={OrderId}", symbol, orderId);
            return false;
        }
    }
}

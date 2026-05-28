using System.Collections.Concurrent;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 交易执行器，统一的下单入口：风控 → 确认 → 下单 → 记录 → PnL 计算。
/// 同一交易对在任意时刻仅允许一条下单路径进入交易所调用，避免并发重复下单。
/// </summary>
public class TradeExecutor
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _symbolExecutionLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IExchangeClient _exchangeClient;
    private readonly RiskManager _riskManager;
    private readonly TradingDataService _dataService;
    private readonly ILogger<TradeExecutor> _logger;

    /// <summary>
    /// Human-in-the-Loop 确认回调。
    /// 当风控返回 NeedsConfirmation 时，调用此回调等待用户确认。
    /// 参数: (symbol, side, quantity, price, reason) → true=放行 false=拒绝。
    /// 未设置时保持现有行为（直接拒绝）。
    /// </summary>
    public Func<string, OrderSide, decimal, decimal, string, Task<bool>>? ConfirmationCallback { get; set; }

    public TradeExecutor(
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        RiskManager riskManager,
        TradingDataService dataService,
        ILogger<TradeExecutor> logger)
    {
        _exchangeClient = exchangeClient;
        _riskManager = riskManager;
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// 执行策略触发的交易（委托给通用下单方法）
    /// </summary>
    public async Task<TradeResult> ExecuteTradeAsync(
        TradingStrategy strategy, decimal currentPrice, string? aiReasoning = null,
        string? pendingCustomParams = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("开始执行交易: {StrategyId} {Symbol} {Side} 数量:{Qty}",
            strategy.Id, strategy.Symbol, strategy.Side, strategy.Quantity);

        var result = await ExecuteOrderAsync(
            strategy.Symbol, strategy.Side, OrderType.Market, strategy.Quantity,
            currentPrice, limitPrice: null, strategyId: strategy.Id,
            aiReasoning: aiReasoning, ct: ct);

        if (result.Success)
        {
            if (pendingCustomParams != null)
                await _dataService.UpdateStrategyTriggeredWithParamsAsync(strategy.Id, pendingCustomParams, ct);
            else
                await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct);
        }

        return result;
    }

    /// <summary>
    /// 通用下单方法，所有交易路径（策略触发、AI Agent、手动）的统一入口。
    /// 风控检查和人工确认在 symbol 锁之外执行，避免等待用户输入时锁死后续交易。
    /// </summary>
    public async Task<TradeResult> ExecuteOrderAsync(
        string instrumentSymbol, OrderSide side, OrderType type, decimal quantity,
        decimal currentPrice, decimal? limitPrice = null,
        string strategyId = "manual", string? aiReasoning = null,
        CancellationToken ct = default)
    {
        // 风控校验和人工确认在 symbol 锁外完成，防止 ConfirmationCallback 等待期间
        // 持有 SemaphoreSlim，导致同一标的后续所有交易永久阻塞。
        var riskCheck = await _riskManager.ValidateOrderAsync(instrumentSymbol, side, quantity, currentPrice, type, ct);

        if (riskCheck.NeedsConfirmation)
        {
            _logger.LogWarning("交易需人工确认: {InstrumentSymbol} {Side} 金额:{Amount}",
                instrumentSymbol, side, quantity * currentPrice);

            if (ConfirmationCallback != null)
            {
                var approved = await ConfirmationCallback(
                    instrumentSymbol, side, quantity, currentPrice, riskCheck.Reason ?? "需人工确认");
                if (!approved)
                    return new TradeResult { Success = false, ErrorMessage = $"用户拒绝交易: {riskCheck.Reason}" };

                _logger.LogInformation("用户已确认交易: {InstrumentSymbol} {Side}", instrumentSymbol, side);
            }
            else
            {
                return new TradeResult { Success = false, ErrorMessage = $"需人工确认: {riskCheck.Reason}" };
            }
        }
        else if (!riskCheck.Passed)
        {
            _logger.LogWarning("风控拒绝: {Reason}", riskCheck.Reason);
            return new TradeResult { Success = false, ErrorMessage = $"风控拒绝: {riskCheck.Reason}" };
        }

        // 仅在实际调用交易所 API 时持有 symbol 锁，防止同一标的并发重复下单
        var gate = _symbolExecutionLocks.GetOrAdd(
            instrumentSymbol.Trim(), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ExecuteApprovedOrderAsync(
                instrumentSymbol, side, type, quantity, currentPrice, limitPrice,
                strategyId, aiReasoning, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TradeResult> ExecuteApprovedOrderAsync(
        string instrumentSymbol, OrderSide side, OrderType type, decimal quantity,
        decimal currentPrice, decimal? limitPrice,
        string strategyId, string? aiReasoning,
        CancellationToken ct)
    {
        try
        {
            var response = await _exchangeClient.PlaceOrderAsync(
                instrumentSymbol, side, type, quantity, type == OrderType.Limit ? limitPrice : null, ct);

            var record = new TradeRecord
            {
                StrategyId = strategyId,
                Symbol = instrumentSymbol,
                Side = side,
                OrderType = type,
                RequestedQty = response.RequestedQty == 0 ? quantity : response.RequestedQty,
                ExecutedQty = response.ExecutedQty,
                RequestedPrice = limitPrice,
                ExecutedPrice = response.Price == 0 ? currentPrice : response.Price,
                Status = MapStatus(response.Status),
                BinanceOrderId = long.TryParse(response.OrderId, out var orderId) ? orderId : 0,
                AIReasoning = aiReasoning,
                CompletedAt = response.Status == "FILLED" ? DateTime.UtcNow : null
            };

            await _dataService.SaveTradeRecordAsync(record, ct);

            decimal pnl = 0;
            if (record.ExecutedQty > 0 && side == OrderSide.Sell)
            {
                // 现货多头平仓：卖出价 - 加权平均买入价
                var avgEntryPrice = await _dataService.GetAverageEntryPriceAsync(instrumentSymbol, ct);
                if (avgEntryPrice > 0)
                    pnl = (record.ExecutedPrice - avgEntryPrice) * record.ExecutedQty;
            }
            // 现货买入为开多仓，无已实现盈亏
            await _dataService.UpdateDailyStatsAsync(pnl, record.Commission, ct);

            _logger.LogInformation("交易执行成功: {StrategyId} 订单ID:{OrderId} 状态:{Status} PnL:{Pnl}",
                strategyId, response.OrderId, response.Status, pnl);

            return new TradeResult { Success = true, Record = record };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "交易执行失败: {InstrumentSymbol} {Side}", instrumentSymbol, side);
            return new TradeResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static TradeRecordStatus MapStatus(string status) => status switch
    {
        "FILLED" => TradeRecordStatus.Filled,
        "PARTIALLY_FILLED" => TradeRecordStatus.PartiallyFilled,
        "CANCELED" or "CANCELLED" => TradeRecordStatus.Cancelled,
        "REJECTED" or "EXPIRED" => TradeRecordStatus.Failed,
        _ => TradeRecordStatus.Pending
    };
}

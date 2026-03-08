using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 交易执行器，统一的下单入口：风控 → 下单 → 记录 → PnL 计算
/// </summary>
public class TradeExecutor
{
    private readonly BinanceAccountService _accountService;
    private readonly RiskManager _riskManager;
    private readonly TradingDataService _dataService;
    private readonly ILogger<TradeExecutor> _logger;

    public TradeExecutor(
        BinanceAccountService accountService,
        RiskManager riskManager,
        TradingDataService dataService,
        ILogger<TradeExecutor> logger)
    {
        _accountService = accountService;
        _riskManager = riskManager;
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// 执行策略触发的交易（委托给通用下单方法）
    /// </summary>
    public async Task<TradeResult> ExecuteTradeAsync(
        TradingStrategy strategy, decimal currentPrice, string? aiReasoning = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("开始执行交易: {StrategyId} {Symbol} {Side} 数量:{Qty}",
            strategy.Id, strategy.Symbol, strategy.Side, strategy.Quantity);

        var result = await ExecuteOrderAsync(
            strategy.Symbol, strategy.Side, OrderType.Market, strategy.Quantity,
            currentPrice, limitPrice: null, strategyId: strategy.Id,
            aiReasoning: aiReasoning, ct: ct);

        if (result.Success)
            await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct);

        return result;
    }

    /// <summary>
    /// 通用下单方法，所有交易路径（策略触发、AI Agent、手动）的统一入口
    /// </summary>
    public async Task<TradeResult> ExecuteOrderAsync(
        string symbol, OrderSide side, OrderType type, decimal quantity,
        decimal currentPrice, decimal? limitPrice = null,
        string strategyId = "manual", string? aiReasoning = null,
        CancellationToken ct = default)
    {
        var riskCheck = await _riskManager.ValidateOrderAsync(symbol, side, quantity, currentPrice, ct);

        if (riskCheck.NeedsConfirmation)
        {
            _logger.LogWarning("交易需人工确认: {Symbol} {Side} 金额:{Amount}",
                symbol, side, quantity * currentPrice);
            return new TradeResult { Success = false, ErrorMessage = $"需人工确认: {riskCheck.Reason}" };
        }

        if (!riskCheck.Passed)
        {
            _logger.LogWarning("风控拒绝: {Reason}", riskCheck.Reason);
            return new TradeResult { Success = false, ErrorMessage = $"风控拒绝: {riskCheck.Reason}" };
        }

        try
        {
            var orderTypeStr = type.ToString().ToUpper();
            var response = await _accountService.PlaceOrderAsync(
                symbol, side.ToString().ToUpper(), orderTypeStr, quantity,
                type == OrderType.Limit ? limitPrice : null);

            var record = new TradeRecord
            {
                StrategyId = strategyId,
                Symbol = symbol,
                Side = side,
                OrderType = type,
                RequestedQty = quantity,
                ExecutedQty = decimal.TryParse(response.ExecutedQty, out var eq) ? eq : 0,
                RequestedPrice = limitPrice,
                ExecutedPrice = decimal.TryParse(response.Price, out var ep) ? ep : currentPrice,
                Status = MapStatus(response.Status),
                BinanceOrderId = response.OrderId,
                AIReasoning = aiReasoning,
                CompletedAt = response.Status == "FILLED" ? DateTime.UtcNow : null
            };

            await _dataService.SaveTradeRecordAsync(record, ct);

            decimal pnl = 0;
            if (side == OrderSide.Sell && record.ExecutedQty > 0)
            {
                var avgEntryPrice = await _dataService.GetAverageEntryPriceAsync(symbol, ct);
                if (avgEntryPrice > 0)
                    pnl = (record.ExecutedPrice - avgEntryPrice) * record.ExecutedQty;
            }
            await _dataService.UpdateDailyStatsAsync(pnl, record.Commission, ct);

            _logger.LogInformation("交易执行成功: {StrategyId} 订单ID:{OrderId} 状态:{Status} PnL:{Pnl}",
                strategyId, response.OrderId, response.Status, pnl);

            return new TradeResult { Success = true, Record = record };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "交易执行失败: {Symbol} {Side}", symbol, side);
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

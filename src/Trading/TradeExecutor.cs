using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 交易执行器，封装下单流程：风控 → 下单 → 记录
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
    /// 执行策略触发的交易
    /// </summary>
    public async Task<TradeResult> ExecuteTradeAsync(
        TradingStrategy strategy, decimal currentPrice, string? aiReasoning = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("开始执行交易: {StrategyId} {Symbol} {Side} 数量:{Qty}",
            strategy.Id, strategy.Symbol, strategy.Side, strategy.Quantity);

        var riskCheck = await _riskManager.ValidateOrderAsync(
            strategy.Symbol, strategy.Side, strategy.Quantity, currentPrice, ct);

        if (!riskCheck.Passed)
        {
            _logger.LogWarning("风控拒绝策略 {StrategyId}: {Reason}", strategy.Id, riskCheck.Reason);
            return new TradeResult { Success = false, ErrorMessage = $"风控拒绝: {riskCheck.Reason}" };
        }

        try
        {
            var orderType = strategy.TriggerPrice > 0 ? "MARKET" : "LIMIT";
            var response = await _accountService.PlaceOrderAsync(
                strategy.Symbol,
                strategy.Side.ToString().ToUpper(),
                orderType,
                strategy.Quantity,
                orderType == "LIMIT" ? strategy.TriggerPrice : null);

            var record = new TradeRecord
            {
                StrategyId = strategy.Id,
                Symbol = strategy.Symbol,
                Side = strategy.Side,
                OrderType = orderType == "MARKET" ? OrderType.Market : OrderType.Limit,
                RequestedQty = strategy.Quantity,
                ExecutedQty = decimal.TryParse(response.ExecutedQty, out var eq) ? eq : 0,
                RequestedPrice = strategy.TriggerPrice,
                ExecutedPrice = decimal.TryParse(response.Price, out var ep) ? ep : currentPrice,
                Status = MapStatus(response.Status),
                BinanceOrderId = response.OrderId,
                AIReasoning = aiReasoning,
                CompletedAt = response.Status == "FILLED" ? DateTime.UtcNow : null
            };

            // 计算手续费（从订单响应中暂时无法获取，设为 0）
            await _dataService.SaveTradeRecordAsync(record, ct);
            await _dataService.UpdateStrategyTriggeredAsync(strategy.Id, ct);

            var pnl = 0m; // 简化：PnL 在后续版本中基于成本计算
            await _dataService.UpdateDailyStatsAsync(pnl, record.Commission, ct);

            _logger.LogInformation("交易执行成功: {StrategyId} 订单ID:{OrderId} 状态:{Status}",
                strategy.Id, response.OrderId, response.Status);

            return new TradeResult { Success = true, Record = record };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "交易执行失败: {StrategyId}", strategy.Id);
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

using System.ComponentModel;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 策略管理工具实现，供 TradingAgent 查询和更新策略
/// </summary>
public class CryptoStrategyTools : IStrategyTools
{
    private readonly TradingStrategyService _strategyService;
    private readonly TradingDataService _dataService;
    private readonly ILogger<CryptoStrategyTools> _logger;

    public CryptoStrategyTools(
        TradingStrategyService strategyService,
        TradingDataService dataService,
        ILogger<CryptoStrategyTools> logger)
    {
        _strategyService = strategyService;
        _dataService = dataService;
        _logger = logger;
    }

    [Description("获取所有活跃状态的交易策略列表")]
    public async Task<List<TradingStrategy>> GetActiveStrategiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active, cancellationToken);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取活跃策略列表失败");
            throw new FriendlyException($"获取策略列表失败: {ex.Message}", ex);
        }
    }

    [Description("根据策略ID获取策略详情")]
    public async Task<TradingStrategy?> GetStrategyAsync(
        [Description("策略ID")] string strategyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dataService.GetStrategyAsync(strategyId, cancellationToken);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取策略详情失败: {StrategyId}", strategyId);
            throw new FriendlyException($"获取策略详情失败: {ex.Message}", ex);
        }
    }

    [Description("更新策略状态（如暂停、完成、失败）")]
    public async Task UpdateStrategyStatusAsync(
        [Description("策略ID")] string strategyId,
        [Description("新状态")] StrategyStatus status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _strategyService.UpdateStrategyStatusAsync(strategyId, status, cancellationToken);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "更新策略状态失败: {StrategyId} -> {Status}", strategyId, status);
            throw new FriendlyException($"更新策略状态失败: {ex.Message}", ex);
        }
    }

    [Description("为已有持仓创建护栏策略（止损/止盈/追踪止损），保护当前仓位。返回创建的策略 ID 列表描述。")]
    public async Task<string> CreateGuardrailAsync(
        [Description("交易对符号，如 BTCUSDT")] string symbol,
        [Description("止损价（可选，多头持仓填低于现价的卖出触发价）")] decimal? stopLossPrice,
        [Description("止盈价（可选，多头持仓填高于现价的卖出触发价）")] decimal? takeProfitPrice,
        [Description("追踪止损回调百分比（可选，0-100，如 5 表示从峰值回撤 5% 卖出）")] decimal? trailingPercent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            symbol = symbol.ToUpperInvariant().Trim();
            var positions = await _dataService.GetOpenPositionsAsync(symbol, cancellationToken);
            var totalQty = positions.Sum(p => p.Quantity - p.ClosedQuantity);
            if (totalQty <= 0)
                return $"无法创建护栏：{symbol} 当前无本地持仓记录。";

            var created = new List<string>();

            if (stopLossPrice is > 0)
            {
                var guard = new TradingStrategy
                {
                    Symbol = symbol,
                    Type = StrategyType.StopLoss,
                    Status = StrategyStatus.Active,
                    Side = OrderSide.Sell,
                    TriggerPrice = stopLossPrice.Value,
                    Quantity = totalQty,
                    MaxExecutions = 1
                };
                await _strategyService.SaveStrategyAsync(guard, cancellationToken);
                created.Add($"止损策略 {guard.Id}（触发价 {stopLossPrice}）");
            }

            if (takeProfitPrice is > 0)
            {
                var guard = new TradingStrategy
                {
                    Symbol = symbol,
                    Type = StrategyType.TakeProfit,
                    Status = StrategyStatus.Active,
                    Side = OrderSide.Sell,
                    TriggerPrice = takeProfitPrice.Value,
                    Quantity = totalQty,
                    MaxExecutions = 1
                };
                await _strategyService.SaveStrategyAsync(guard, cancellationToken);
                created.Add($"止盈策略 {guard.Id}（触发价 {takeProfitPrice}）");
            }

            if (trailingPercent is > 0 and <= 100)
            {
                var guard = new TradingStrategy
                {
                    Symbol = symbol,
                    Type = StrategyType.TrailingStop,
                    Status = StrategyStatus.Active,
                    Side = OrderSide.Sell,
                    Quantity = totalQty,
                    MaxExecutions = 1,
                    CustomParams = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        trailingPercent = trailingPercent.Value
                    })
                };
                await _strategyService.SaveStrategyAsync(guard, cancellationToken);
                created.Add($"追踪止损策略 {guard.Id}（回调 {trailingPercent}%，立即激活）");
            }

            if (created.Count == 0)
                return "未创建任何护栏：请至少提供止损价、止盈价或追踪止损回调百分比之一。";

            _logger.LogInformation("已为 {Symbol} 创建护栏: {Summary}", symbol, string.Join("; ", created));
            return $"已为 {symbol}（持仓 {totalQty}）创建护栏：{string.Join("; ", created)}";
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "创建护栏策略失败: {Symbol}", symbol);
            throw new FriendlyException($"创建护栏策略失败: {ex.Message}", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetActiveStrategiesAsync);
        yield return AIFunctionFactory.Create(GetStrategyAsync);
        yield return AIFunctionFactory.Create(UpdateStrategyStatusAsync);
        yield return AIFunctionFactory.Create(CreateGuardrailAsync);
    }
}

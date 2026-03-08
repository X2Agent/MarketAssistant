using MarketAssistant.Applications.Crypto;
using MarketAssistant.Services.Data;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Trading;

/// <summary>
/// 风控网关，所有交易指令必须经过风控检查
/// </summary>
public class RiskManager
{
    private readonly TradingDataService _dataService;
    private readonly BinanceAccountService _accountService;
    private readonly BinanceMarketDataService _marketDataService;
    private readonly ILogger<RiskManager> _logger;

    public RiskManager(
        TradingDataService dataService,
        BinanceAccountService accountService,
        BinanceMarketDataService marketDataService,
        ILogger<RiskManager> logger)
    {
        _dataService = dataService;
        _accountService = accountService;
        _marketDataService = marketDataService;
        _logger = logger;
    }

    /// <summary>
    /// 校验交易是否通过风控检查
    /// </summary>
    public async Task<RiskCheckResult> ValidateOrderAsync(
        string symbol, OrderSide side, decimal quantity, decimal price,
        CancellationToken ct = default)
    {
        var config = _dataService.LoadRiskConfig();
        var orderValueUSDT = quantity * price;

        if (orderValueUSDT < config.MinOrderAmount)
            return RiskCheckResult.Reject($"订单金额 {orderValueUSDT:F2} USDT 低于最小限额 {config.MinOrderAmount} USDT");

        var todayStats = await _dataService.GetTodayStatsAsync(ct);

        if (todayStats.TradeCount >= config.MaxDailyTrades)
            return RiskCheckResult.Reject($"今日交易次数 {todayStats.TradeCount} 已达上限 {config.MaxDailyTrades}");

        BinanceAccountInfo accountInfo;
        try
        {
            accountInfo = await _accountService.GetAccountInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取账户信息用于风控校验失败，拒绝交易（fail-closed）");
            return RiskCheckResult.Reject("无法获取账户信息，风控拒绝交易");
        }

        var totalUSDT = await CalculateTotalValueUSDTAsync(accountInfo);

        if (totalUSDT > 0)
        {
            var orderPercent = orderValueUSDT / totalUSDT * 100;
            if (orderPercent > config.MaxSingleOrderPercent)
                return RiskCheckResult.Reject(
                    $"单笔金额占比 {orderPercent:F1}% 超过限额 {config.MaxSingleOrderPercent}%");

            if (config.MaxTotalPositionPercent > 0)
            {
                var usdtBalance = CalculateUSDTBalance(accountInfo);
                var nonUSDTValue = totalUSDT - usdtBalance;
                var currentPositionPercent = nonUSDTValue / totalUSDT * 100;
                var projectedPercent = currentPositionPercent + (orderValueUSDT / totalUSDT * 100);
                if (projectedPercent > config.MaxTotalPositionPercent)
                    return RiskCheckResult.Reject(
                        $"总仓位占比将达 {projectedPercent:F1}%，超过限额 {config.MaxTotalPositionPercent}%");
            }

            var dailyLossPercent = Math.Abs(todayStats.TotalPnl) / totalUSDT * 100;
            if (todayStats.TotalPnl < 0 && dailyLossPercent >= config.MaxDailyLossPercent)
                return RiskCheckResult.Reject(
                    $"今日亏损 {dailyLossPercent:F1}% 已达上限 {config.MaxDailyLossPercent}%");
        }

        if (config.RequireConfirmation && orderValueUSDT >= config.ConfirmationThreshold)
            return RiskCheckResult.RequireConfirmation(
                $"订单金额 {orderValueUSDT:F2} USDT 超过确认阈值 {config.ConfirmationThreshold} USDT，需人工确认");

        _logger.LogInformation("风控检查通过: {Symbol} {Side} 数量:{Qty} 价格:{Price}",
            symbol, side, quantity, price);
        return RiskCheckResult.Pass();
    }

    /// <summary>
    /// 计算账户总资产的 USDT 等值（包括所有币种）
    /// </summary>
    private async Task<decimal> CalculateTotalValueUSDTAsync(BinanceAccountInfo accountInfo)
    {
        decimal total = 0;
        foreach (var balance in accountInfo.Balances)
        {
            if (!decimal.TryParse(balance.Free, out var free) || !decimal.TryParse(balance.Locked, out var locked))
                continue;
            var amount = free + locked;
            if (amount <= 0) continue;

            if (balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            {
                total += amount;
            }
            else
            {
                try
                {
                    var ticker = await _marketDataService.Get24hrTickerAsync($"{balance.Asset}USDT");
                    if (ticker != null)
                        total += amount * ticker.LastPrice;
                }
                catch { /* 无法交易的币对跳过 */ }
            }
        }
        return total;
    }

    private static decimal CalculateUSDTBalance(BinanceAccountInfo accountInfo)
    {
        foreach (var balance in accountInfo.Balances)
        {
            if (balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase) &&
                decimal.TryParse(balance.Free, out var free) && decimal.TryParse(balance.Locked, out var locked))
                return free + locked;
        }
        return 0;
    }
}

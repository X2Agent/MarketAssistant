using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using System.Globalization;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// Binance 合约交易所适配器，继承自 BinanceExchangeClient 并覆写 GetPositionsAsync 和 SetLeverageAsync。
/// 单向模式下所有订单 positionSide 默认为 BOTH。
/// </summary>
public sealed class BinanceFuturesExchangeClient : BinanceExchangeClient
{
    private readonly BinanceFuturesAccountService _futuresAccountService;

    public BinanceFuturesExchangeClient(
        BinanceFuturesAccountService accountService,
        string exchangeName)
        : base(accountService, exchangeName, "BOTH")
    {
        _futuresAccountService = accountService;
    }

    /// <inheritdoc />
    public override async Task<List<ExchangePosition>> GetPositionsAsync(
        string? instrumentSymbol = null, CancellationToken ct = default)
    {
        var positions = await _futuresAccountService.GetPositionInfoAsync(instrumentSymbol, ct);

        return positions
            .Where(p => decimal.TryParse(p.PositionAmt, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt) && amt != 0)
            .Select(p => new ExchangePosition
            {
                Symbol = p.Symbol,
                PositionSide = p.PositionSide,
                PositionAmt = decimal.TryParse(p.PositionAmt, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt) ? amt : 0,
                EntryPrice = decimal.TryParse(p.EntryPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var ep) ? ep : 0,
                MarkPrice = decimal.TryParse(p.MarkPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var mp) ? mp : 0,
                UnRealizedProfit = decimal.TryParse(p.UnRealizedProfit, NumberStyles.Number, CultureInfo.InvariantCulture, out var up) ? up : 0,
                Leverage = decimal.TryParse(p.Leverage, NumberStyles.Number, CultureInfo.InvariantCulture, out var lev) ? lev : 0,
                MarginType = p.MarginType == "isolated" ? "isolated" : "cross",
                MaxQty = decimal.TryParse(p.PositionAmt, NumberStyles.Number, CultureInfo.InvariantCulture, out var pa) ? Math.Abs(pa) : 0
            }).ToList();
    }

    /// <summary>
    /// 设置合约杠杆倍数。合约交易前必须设置杠杆，否则使用默认值（通常 20x），有强平风险。
    /// </summary>
    public override async Task SetLeverageAsync(string instrumentSymbol, int leverage, CancellationToken ct = default)
    {
        if (leverage < 1 || leverage > 125)
            throw new ArgumentOutOfRangeException(nameof(leverage), "合约杠杆倍数必须在 1-125 之间");

        await _futuresAccountService.SetLeverageAsync(instrumentSymbol, leverage, ct);
    }
}

using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// 虚拟币行情速览：复用币安 24 小时统计接口。
/// </summary>
public sealed class CryptoMarketSnapshotService(
    BinanceMarketDataService binanceService,
    ILogger<CryptoMarketSnapshotService> logger) : IMarketSnapshotService
{
    private readonly BinanceMarketDataService _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
    private readonly ILogger<CryptoMarketSnapshotService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<MarketSnapshot?> GetSnapshotAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var symbol = CryptoSymbolConverter.ToBinanceFormat(code);
        var ticker = await _binanceService.Get24hrTickerAsync(symbol, cancellationToken);
        if (ticker is null)
        {
            _logger.LogInformation("币安未返回 {Symbol} 的 24 小时统计", symbol);
            return null;
        }

        // 币安不提供换手率/振幅/量比/PE/PB；振幅可由最高最低与昨收推导
        return new MarketSnapshot(
            Open: ZeroAsNull(ticker.OpenPrice),
            High: ZeroAsNull(ticker.HighPrice),
            Low: ZeroAsNull(ticker.LowPrice),
            PreClose: ZeroAsNull(ticker.PrevClosePrice ?? 0),
            Volume: ZeroAsNull(ticker.Volume),
            Amount: ZeroAsNull(ticker.QuoteVolume),
            Amplitude: DeriveAmplitude(ticker));
    }

    /// <summary>振幅 = (最高 - 最低) / 昨收 × 100。</summary>
    private static decimal? DeriveAmplitude(Binance24hrTicker ticker)
    {
        var preClose = ticker.PrevClosePrice ?? 0;
        if (preClose <= 0 || ticker.HighPrice <= 0 || ticker.LowPrice <= 0)
            return null;

        return (ticker.HighPrice - ticker.LowPrice) / preClose * 100;
    }

    private static decimal? ZeroAsNull(decimal value) => value == 0 ? null : value;
}

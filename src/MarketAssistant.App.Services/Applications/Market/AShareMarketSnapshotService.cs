using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// A 股行情速览：复用财联社 <c>/quote/stock/basic</c>（与资产信息同一数据源，仅扩字段）。
/// </summary>
public sealed class AShareMarketSnapshotService(
    ClsQuoteClient clsClient,
    ILogger<AShareMarketSnapshotService> logger) : IMarketSnapshotService
{
    /// <summary>
    /// 请求字段列表。财联社按此列表裁剪返回，字段越少响应越小。
    /// </summary>
    private const string QuoteFields =
        "secu_name,secu_code,last_px,change,open_px,high_px,low_px,preclose_px," +
        "business_amount,business_balance,tr,amp,qrr,pe,pb";

    private readonly ClsQuoteClient _clsClient = clsClient ?? throw new ArgumentNullException(nameof(clsClient));
    private readonly ILogger<AShareMarketSnapshotService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<MarketSnapshot?> GetSnapshotAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var clsCode = StockSymbolConverter.ToClsFormat(code);
        if (string.IsNullOrEmpty(clsCode))
            return null;

        var data = await _clsClient.GetStockQuoteAsync(clsCode, QuoteFields, cancellationToken);
        if (data is null)
            return null;

        var snapshot = new MarketSnapshot(
            Open: ZeroAsNull(data.OpenPx),
            High: ZeroAsNull(data.HighPx),
            Low: ZeroAsNull(data.LowPx),
            PreClose: ZeroAsNull(data.PreClosePx),
            Volume: ZeroAsNull(data.BusinessAmount),
            Amount: ZeroAsNull(data.BusinessBalance),
            TurnoverRate: ZeroAsNull(data.TurnoverRate),
            Amplitude: ZeroAsNull(data.Amplitude),
            VolumeRatio: ZeroAsNull(data.VolumeRatio),
            Pe: ZeroAsNull(data.PERatio),
            Pb: ZeroAsNull(data.PBRatio));

        if (!snapshot.HasAnyValue)
        {
            _logger.LogInformation("财联社未返回 {Code} 的行情速览字段", clsCode);
            return null;
        }

        return snapshot;
    }

    /// <summary>
    /// 财联社对缺失字段返回 0；行情类字段中 0 不代表有效值，统一视为「未提供」。
    /// </summary>
    private static decimal? ZeroAsNull(decimal value) => value == 0 ? null : value;
}

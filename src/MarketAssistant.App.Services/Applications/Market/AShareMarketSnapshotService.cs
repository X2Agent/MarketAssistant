using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// A 股行情速览：优先财联社 <c>/quote/stock/basic</c>（字段最全，含换手/量比/市盈率）；
/// 财联社对基金/ETF 返回全 null（实测），回退新浪个股行情接口的开高低收与成交量额。
/// </summary>
/// <remarks>
/// 兜底路径只能提供 今开/最高/最低/昨收/成交量/成交额 与据此计算的振幅；
/// 换手率/量比/市盈率/市净率留 null（UI 按「未提供」隐藏），不伪造数值。
/// </remarks>
public sealed class AShareMarketSnapshotService(
    ClsQuoteClient clsClient,
    SinaStockQuoteClient sinaClient,
    ILogger<AShareMarketSnapshotService> logger) : IMarketSnapshotService
{
    /// <summary>
    /// 请求字段列表。财联社按此列表裁剪返回，字段越少响应越小。
    /// </summary>
    private const string QuoteFields =
        "secu_name,secu_code,last_px,change,open_px,high_px,low_px,preclose_px," +
        "business_amount,business_balance,tr,amp,qrr,pe,pb";

    private readonly ClsQuoteClient _clsClient = clsClient ?? throw new ArgumentNullException(nameof(clsClient));
    private readonly SinaStockQuoteClient _sinaClient = sinaClient ?? throw new ArgumentNullException(nameof(sinaClient));
    private readonly ILogger<AShareMarketSnapshotService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<MarketSnapshot?> GetSnapshotAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var clsSnapshot = await GetFromClsAsync(code, cancellationToken);
        if (clsSnapshot is not null)
            return clsSnapshot;

        var sinaSnapshot = await GetFromSinaAsync(code, cancellationToken);
        if (sinaSnapshot is null)
        {
            _logger.LogInformation("财联社与新浪均未返回 {Code} 的行情速览字段", code);
            return null;
        }

        return sinaSnapshot;
    }

    /// <summary>财联社字段最全；返回 null（含解析异常）时由调用方回退新浪。</summary>
    private async Task<MarketSnapshot?> GetFromClsAsync(string code, CancellationToken cancellationToken)
    {
        var clsCode = StockSymbolConverter.ToClsFormat(code);
        if (string.IsNullOrEmpty(clsCode))
            return null;

        try
        {
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

            return snapshot.HasAnyValue ? snapshot : null;
        }
        catch (Exception ex)
        {
            // 财联社字段漂移/网络抖动不应冒泡：回退新浪即可，错误进日志
            _logger.LogInformation(ex, "财联社获取 {Code} 的行情速览失败，回退新浪数据源", clsCode);
            return null;
        }
    }

    /// <summary>
    /// 新浪兜底：开高低收/成交量额直接取，振幅按 (最高-最低)/昨收 计算。
    /// </summary>
    private async Task<MarketSnapshot?> GetFromSinaAsync(string code, CancellationToken cancellationToken)
    {
        var sinaCode = StockSymbolConverter.ToClsFormat(code);
        if (string.IsNullOrEmpty(sinaCode))
            return null;

        var quote = await _sinaClient.GetStockQuoteAsync(sinaCode, cancellationToken);
        if (quote is null)
            return null;

        var snapshot = new MarketSnapshot(
            Open: quote.Open,
            High: quote.High,
            Low: quote.Low,
            PreClose: quote.PreClose,
            Volume: quote.Volume,
            Amount: quote.Amount,
            Amplitude: CalculateAmplitude(quote.High, quote.Low, quote.PreClose));

        return snapshot.HasAnyValue ? snapshot : null;
    }

    /// <summary>振幅 = (最高-最低)/昨收×100%；任一字段缺失时返回 null。</summary>
    private static decimal? CalculateAmplitude(decimal? high, decimal? low, decimal? preClose) =>
        high is > 0m && low is > 0m && preClose is > 0m
            ? Math.Round((high.Value - low.Value) / preClose.Value * 100m, 2)
            : null;

    /// <summary>
    /// 财联社对缺失字段可能返回 0 或 null；行情类字段中 0 不代表有效值，统一视为「未提供」。
    /// </summary>
    private static decimal? ZeroAsNull(decimal? value) => value is null or 0m ? null : value;
}

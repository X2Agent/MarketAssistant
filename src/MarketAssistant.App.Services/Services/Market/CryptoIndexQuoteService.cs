using MarketAssistant.Applications.Assets;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 虚拟币顶栏行情条：全网总市值+24h涨跌幅、BTC 主导率（CoinGecko /global 单次请求）
/// 与恐惧贪婪指数（alternative.me）。三项均为市场级聚合指标，非具体标的价格。
/// 任一数据源失败仅省略对应项，全部失败抛出，由 UI 区分故障与正常无数据。
/// </summary>
public sealed class CryptoIndexQuoteService(CoinGeckoApiService coinGecko, AlternativeMeClient alternativeMe) : IIndexQuoteService
{
    private readonly CoinGeckoApiService _coinGecko = coinGecko;
    private readonly AlternativeMeClient _alternativeMe = alternativeMe;

    public async Task<IReadOnlyList<IndexQuoteItem>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        // 两个独立数据源并行拉取，互不阻塞
        var globalTask = CaptureAsync(() => _coinGecko.GetGlobalStatsAsync(cancellationToken));
        var fngTask = CaptureAsync(() => _alternativeMe.GetLatestAsync(cancellationToken));
        await Task.WhenAll(globalTask, fngTask);

        var globalResult = globalTask.Result;
        var fngResult = fngTask.Result;
        if (globalResult.Error != null && fngResult.Error != null)
        {
            throw new FriendlyException(
                "虚拟币市场指标数据源均不可用",
                new AggregateException(globalResult.Error, fngResult.Error));
        }

        var items = new List<IndexQuoteItem>(3);

        if (globalResult.Value is { } global)
        {
            // 全网总市值 + 24h 涨跌幅
            items.Add(new IndexQuoteItem(
                "总市值",
                FormatUsdCompact(global.TotalMarketCapUsd),
                PercentFormatter.Format(global.MarketCapChangePct24h),
                TickerTrendExtensions.FromChange(global.MarketCapChangePct24h)));

            // BTC 主导率（无涨跌语义，仅展示占比）
            items.Add(new IndexQuoteItem("BTC主导率", $"{global.BtcDominancePct:F1}%", null, TickerTrend.None));
        }

        if (fngResult.Value is { } fng)
        {
            // 恐惧贪婪指数：数值 + 中文分类，无涨跌语义
            items.Add(new IndexQuoteItem("恐惧贪婪", fng.Value.ToString(), TranslateFng(fng.Classification), TickerTrend.None));
        }

        return items;
    }

    /// <summary>保留单源故障信息，使单源可降级、全源可识别；取消始终向外传播。</summary>
    private static async Task<(T? Value, Exception? Error)> CaptureAsync<T>(Func<Task<T?>> action) where T : struct
    {
        try
        {
            return (await action(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    /// <summary>美元大数缩写：$2.41T / $98.5B / $1.2M。</summary>
    private static string FormatUsdCompact(decimal usd)
    {
        var abs = Math.Abs(usd);
        if (abs >= 1_000_000_000_000m) return $"${usd / 1_000_000_000_000m:F2}T";
        if (abs >= 1_000_000_000m) return $"${usd / 1_000_000_000m:F2}B";
        if (abs >= 1_000_000m) return $"${usd / 1_000_000m:F2}M";
        return $"${usd:N0}";
    }

    /// <summary>恐惧贪婪英文分类 → 中文短词。</summary>
    private static string TranslateFng(string classification) => classification switch
    {
        "Extreme Fear" => "极度恐慌",
        "Fear" => "恐慌",
        "Neutral" => "中性",
        "Greed" => "贪婪",
        "Extreme Greed" => "极度贪婪",
        _ => classification,
    };
}

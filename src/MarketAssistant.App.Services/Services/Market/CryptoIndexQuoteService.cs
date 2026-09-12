using MarketAssistant.DataProviders;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 虚拟币顶栏行情条：全网总市值+24h涨跌幅、BTC 主导率（CoinGecko /global 单次请求）
/// 与恐惧贪婪指数（alternative.me）。三项均为市场级聚合指标，非具体标的价格。
/// 任一数据源失败仅省略对应项，全失败返回空集合（顶栏整体隐藏）。
/// </summary>
public sealed class CryptoIndexQuoteService : IIndexQuoteService
{
    private readonly CoinGeckoApiService _coinGecko;
    private readonly AlternativeMeClient _alternativeMe;

    public CryptoIndexQuoteService(CoinGeckoApiService coinGecko, AlternativeMeClient alternativeMe)
    {
        _coinGecko = coinGecko;
        _alternativeMe = alternativeMe;
    }

    public async Task<IReadOnlyList<IndexQuoteItem>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        // 两个独立数据源并行拉取，互不阻塞
        var globalTask = SafeAsync(() => _coinGecko.GetGlobalStatsAsync(cancellationToken));
        var fngTask = SafeAsync(() => _alternativeMe.GetLatestAsync(cancellationToken));
        await Task.WhenAll(globalTask, fngTask);

        var items = new List<IndexQuoteItem>(3);

        if (globalTask.Result is { } global)
        {
            // 全网总市值 + 24h 涨跌幅
            var capTrend = global.MarketCapChangePct24h > 0 ? TickerTrend.Up
                : global.MarketCapChangePct24h < 0 ? TickerTrend.Down
                : TickerTrend.Flat;
            items.Add(new IndexQuoteItem(
                "总市值",
                FormatUsdCompact(global.TotalMarketCapUsd),
                $"{global.MarketCapChangePct24h:+0.00;-0.00;0.00}%",
                capTrend));

            // BTC 主导率（无涨跌语义，仅展示占比）
            items.Add(new IndexQuoteItem("BTC主导率", $"{global.BtcDominancePct:F1}%", null, TickerTrend.None));
        }

        if (fngTask.Result is { } fng)
        {
            // 恐惧贪婪指数：数值 + 中文分类，无涨跌语义
            items.Add(new IndexQuoteItem("恐惧贪婪", fng.Value.ToString(), TranslateFng(fng.Classification), TickerTrend.None));
        }

        return items;
    }

    /// <summary>吞掉单个数据源异常，返回 null，保证一个源失败不影响其余项。</summary>
    private static async Task<T?> SafeAsync<T>(Func<Task<T?>> action) where T : struct
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
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

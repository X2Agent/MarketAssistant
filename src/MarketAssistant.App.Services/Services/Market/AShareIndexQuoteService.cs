using MarketAssistant.Applications.Assets;
using MarketAssistant.DataProviders.AShare;

namespace MarketAssistant.Services.Market;

/// <summary>
/// A 股顶栏行情条：拉取新浪三大指数（上证/深证成指/创业板指），格式化为点位 + 涨跌幅。
/// </summary>
public sealed class AShareIndexQuoteService(SinaIndexQuoteClient client) : IIndexQuoteService
{
    private readonly SinaIndexQuoteClient _client = client;

    public async Task<IReadOnlyList<IndexQuoteItem>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var quotes = await _client.GetIndexesAsync(cancellationToken);

        var items = new List<IndexQuoteItem>(quotes.Count);
        foreach (var q in quotes)
        {
            items.Add(new IndexQuoteItem(
                q.Name,
                q.Point.ToString("N2"),
                PercentFormatter.Format(q.ChangePercent),
                TickerTrendExtensions.FromChange(q.ChangePercent)));
        }

        return items;
    }
}

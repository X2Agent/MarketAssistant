using MarketAssistant.DataProviders.AShare;

namespace MarketAssistant.Services.Market;

/// <summary>
/// A 股顶栏行情条：拉取新浪三大指数（上证/深证成指/创业板指），格式化为点位 + 涨跌幅。
/// </summary>
public sealed class AShareIndexQuoteService : IIndexQuoteService
{
    private readonly SinaIndexQuoteClient _client;

    public AShareIndexQuoteService(SinaIndexQuoteClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<IndexQuoteItem>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var quotes = await _client.GetIndexesAsync(cancellationToken);

        var items = new List<IndexQuoteItem>(quotes.Count);
        foreach (var q in quotes)
        {
            var trend = q.ChangePercent > 0 ? TickerTrend.Up
                : q.ChangePercent < 0 ? TickerTrend.Down
                : TickerTrend.Flat;

            var changeText = $"{q.ChangePercent:+0.00;-0.00;0.00}%";
            items.Add(new IndexQuoteItem(q.Name, q.Point.ToString("N2"), changeText, trend));
        }

        return items;
    }
}

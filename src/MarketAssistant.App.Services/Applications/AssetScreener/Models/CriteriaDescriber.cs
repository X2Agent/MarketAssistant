using System.Globalization;
using MarketAssistant.Infrastructure.Extensions;

namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 把筛选条件转成一行可读文案，用于选股结果头展示"已应用的筛选条件"，
/// 让用户看到模型把自己的需求译成了什么条件。
/// </summary>
public static class CriteriaDescriber
{
    /// <summary>
    /// 生成形如“行业 半导体 · 总市值 ≥ 100亿 · 市盈率TTM ≤ 40”的文案；没有任何条件时返回空串。
    /// </summary>
    public static string Describe(object? criteria)
    {
        IEnumerable<string> parts = criteria switch
        {
            StockCriteria stock => DescribeStock(stock),
            CryptoCriteria crypto => crypto.Criteria.Select(c => FormatRange(CryptoLabel(c.Code), c.MinValue, c.MaxValue)),
            _ => []
        };

        return string.Join(" · ", parts);
    }

    private static IEnumerable<string> DescribeStock(StockCriteria stock)
    {
        if (stock.Market != AShareType.AllAShares)
        {
            yield return stock.Market.GetDescription();
        }

        if (stock.Industry != IndustryType.All)
        {
            yield return $"行业 {stock.Industry.GetDescription()}";
        }

        foreach (var condition in stock.Criteria)
        {
            var name = string.IsNullOrWhiteSpace(condition.DisplayName) ? condition.Code : condition.DisplayName;
            yield return FormatRange(name, condition.MinValue, condition.MaxValue);
        }
    }

    private static string CryptoLabel(string code) => code.ToLowerInvariant() switch
    {
        "market_cap" => "市值(美元)",
        "market_cap_rank" => "市值排名",
        "volume_24h" or "total_volume" => "24h成交额(美元)",
        "price" or "current_price" => "价格(USDT)",
        "price_change_24h" => "24h涨跌幅(%)",
        "price_change_7d" => "7日涨跌幅(%)",
        "price_change_30d" => "30日涨跌幅(%)",
        _ => code
    };

    private static string FormatRange(string name, decimal? min, decimal? max) => (min, max) switch
    {
        ({ } low, { } high) => $"{name} {FormatNumber(low)}~{FormatNumber(high)}",
        ({ } low, null) => $"{name} ≥ {FormatNumber(low)}",
        (null, { } high) => $"{name} ≤ {FormatNumber(high)}",
        _ => name
    };

    /// <summary>
    /// 大数换算成亿/万，避免界面出现 30000000000 这类难读的数字。
    /// </summary>
    private static string FormatNumber(decimal value) => Math.Abs(value) switch
    {
        >= 100_000_000m => $"{(value / 100_000_000m).ToString("0.##", CultureInfo.InvariantCulture)}亿",
        >= 10_000m => $"{(value / 10_000m).ToString("0.##", CultureInfo.InvariantCulture)}万",
        _ => value.ToString("0.####", CultureInfo.InvariantCulture)
    };
}

using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 价格展示格式化工具（按市场选择小数位），供资产信息服务与实时行情刷新共用。
/// </summary>
public static class PriceFormatter
{
    /// <summary>
    /// A 股按最小报价单位取位：股票 0.01 固定 2 位，基金 / ETF 0.001 固定 3 位
    /// （<paramref name="code"/> 用于识别基金 / ETF，缺省按股票处理）；
    /// 虚拟币价格跨度极大，按量级取位：千及以上 2 位，1 及以上 4 位，小于 1 取 6 位（适配低价币）。
    /// </summary>
    public static string Format(decimal price, MarketType market, string? code = null)
    {
        if (market == MarketType.AShare)
        {
            return price.ToString(StockSymbolConverter.IsFundOrEtf(code) ? "N3" : "N2");
        }

        if (price >= 1000)
        {
            return price.ToString("N2");
        }

        if (price >= 1)
        {
            return price.ToString("N4");
        }

        return price.ToString("N6");
    }
}

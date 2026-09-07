namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 价格展示格式化工具（按价格量级选择小数位），供资产信息服务与实时行情刷新共用。
/// </summary>
public static class PriceFormatter
{
    /// <summary>
    /// 按量级格式化价格：千及以上 2 位小数，1 及以上 4 位，小于 1 取 6 位（适配低价币）。
    /// </summary>
    public static string Format(decimal price)
    {
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
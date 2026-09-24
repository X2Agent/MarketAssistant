using MarketAssistant.Applications.Assets;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// 行情数值展示格式化：缺失值统一返回空串（由界面隐藏该项），
/// 大额数值压缩为「万 / 亿」量级，避免详情页出现一长串数字。
/// </summary>
public static class MarketValueFormatter
{
    /// <summary>价格按量级选择小数位（与实时行情刷新共用 <see cref="PriceFormatter"/>）。</summary>
    public static string FormatPrice(decimal? value) =>
        value.HasValue ? PriceFormatter.Format(value.Value) : string.Empty;

    public static string FormatPercent(decimal? value) =>
        value.HasValue ? $"{value.Value:F2}%" : string.Empty;

    /// <summary>无量纲比值（量比、市盈率、市净率）固定两位小数。</summary>
    public static string FormatRatio(decimal? value) =>
        value.HasValue ? value.Value.ToString("N2") : string.Empty;

    /// <summary>成交量：按万 / 亿量级压缩。</summary>
    public static string FormatQuantity(decimal? value) =>
        value.HasValue ? Compact(value.Value) : string.Empty;

    /// <summary>成交额：按万 / 亿量级压缩。</summary>
    public static string FormatAmount(decimal? value) =>
        value.HasValue ? Compact(value.Value) : string.Empty;

    private static string Compact(decimal value)
    {
        var magnitude = Math.Abs(value);

        if (magnitude >= 100_000_000m)
            return (value / 100_000_000m).ToString("N2") + "亿";

        if (magnitude >= 10_000m)
            return (value / 10_000m).ToString("N2") + "万";

        if (magnitude >= 1m)
            return value.ToString("N2");

        return value.ToString("N4");
    }
}

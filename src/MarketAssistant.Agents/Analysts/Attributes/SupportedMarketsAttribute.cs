using MarketAssistant.Infrastructure.Core;
using System.Reflection;

namespace MarketAssistant.Agents.Analysts.Attributes;

/// <summary>
/// 声明分析师支持的市场类型。缺省该特性表示支持所有市场。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SupportedMarketsAttribute : Attribute
{
    public MarketType[] Markets { get; }

    /// <summary>
    /// 声明支持的市场。
    /// </summary>
    /// <param name="markets">支持的市场类型（至少一个）。</param>
    public SupportedMarketsAttribute(params MarketType[] markets)
    {
        MarketType[] allMarkets = [.. Enum.GetValues<MarketType>()];
        Markets = markets is { Length: > 0 } ? markets : allMarkets;
    }

    /// <summary>
    /// 判断类型是否支持指定市场：未标注特性视为支持所有市场。
    /// </summary>
    public static bool SupportsMarket(Type analystType, MarketType market)
    {
        var attribute = analystType.GetCustomAttribute<SupportedMarketsAttribute>(inherit: false);
        return attribute == null || attribute.Markets.Contains(market);
    }
}
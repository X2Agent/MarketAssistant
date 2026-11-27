using AdaptiveCards;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public interface IJsonToAdaptiveCardParser
{
    /// <summary>
    /// 尝试把 JSON 解析为卡片。不是本解析器负责的类型时返回 false；
    /// 已识别为本类型却反序列化或构卡失败时抛出异常，由调用方统一处理。
    /// </summary>
    bool TryParse(string json, out AdaptiveCard? card);
}

public interface IAdaptiveCardParser<T> : IJsonToAdaptiveCardParser
{
    AdaptiveCard Parse(T model);
}

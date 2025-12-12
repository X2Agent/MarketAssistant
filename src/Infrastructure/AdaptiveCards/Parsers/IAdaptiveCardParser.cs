using AdaptiveCards;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public interface IJsonToAdaptiveCardParser
{
    bool TryParse(string json, out AdaptiveCard? card);
}

public interface IAdaptiveCardParser<T> : IJsonToAdaptiveCardParser
{
    AdaptiveCard Parse(T model);
}

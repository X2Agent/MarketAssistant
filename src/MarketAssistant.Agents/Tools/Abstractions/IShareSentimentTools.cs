using MarketAssistant.Agents.Tools.Models.AShare;

namespace MarketAssistant.Agents.Tools.Abstractions;

public interface IShareSentimentTools : ISentimentTools
{
    Task<FundFlow> GetFundFlowAsync(string assetSymbol, CancellationToken cancellationToken = default);
}







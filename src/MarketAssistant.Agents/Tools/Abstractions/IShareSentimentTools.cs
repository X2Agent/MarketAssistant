using MarketAssistant.Agents.Tools.Models.AShare;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 市场情绪数据工具接口（股票市场专用）
/// </summary>
public interface IShareSentimentTools : ISentimentTools
{
    /// <summary>
    /// 获取资金流向数据
    /// </summary>
    Task<FundFlow> GetFundFlowAsync(string assetSymbol, CancellationToken cancellationToken = default);
}







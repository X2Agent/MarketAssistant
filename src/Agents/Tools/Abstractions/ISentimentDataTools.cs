using MarketAssistant.Agents.Plugins.Models;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 市场情绪数据工具接口
/// </summary>
public interface ISentimentDataTools
{
    /// <summary>
    /// 获取资金流向数据
    /// </summary>
    Task<FundFlow> GetFundFlowAsync(string assetSymbol);

    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}







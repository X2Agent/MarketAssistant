using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 市场情绪分析工具的统一基接口
/// </summary>
/// <remarks>
/// 为不同市场的情绪分析工具提供统一抽象：
/// - A股市场：实现为 IShareSentimentTools（资金流向）
/// - 虚拟币市场：实现为 ICryptoSentimentTools（恐慌贪婪指数、资金费率等）
/// </remarks>
public interface ISentimentTools
{
    /// <summary>
    /// 获取 AI 工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}

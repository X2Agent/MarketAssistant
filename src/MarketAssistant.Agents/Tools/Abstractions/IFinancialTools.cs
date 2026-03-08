using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 财务分析工具的统一基接口
/// </summary>
/// <remarks>
/// 为不同市场的财务分析工具提供统一抽象：
/// - A股市场：实现为 IShareFinancialTools（传统财务报表）
/// - 虚拟币市场：实现为 ICryptoMetricsTools（链上财务指标）
/// </remarks>
public interface IFinancialTools
{
    /// <summary>
    /// 获取 AI 工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}

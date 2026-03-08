using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 筛选条件生成策略接口
/// 用于将用户需求或新闻内容转换为结构化的筛选条件
/// </summary>
public interface ICriteriaGenerationStrategy<TCriteria> where TCriteria : IScreeningCriteria
{
    /// <summary>
    /// 支持的市场类型
    /// </summary>
    MarketType SupportedMarketType { get; }

    /// <summary>
    /// 构建用户需求分析的系统提示词
    /// </summary>
    string BuildUserRequirementSystemPrompt();

    /// <summary>
    /// 构建新闻分析的系统提示词
    /// </summary>
    string BuildNewsAnalysisSystemPrompt();

    /// <summary>
    /// 构建用户提示词
    /// </summary>
    string BuildUserPrompt(InvestmentSelectionWorkflowRequest request);

    /// <summary>
    /// 反序列化筛选条件
    /// </summary>
    TCriteria DeserializeCriteria(string json);
}

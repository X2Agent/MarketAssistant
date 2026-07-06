using System.Text.Json;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;

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

/// <summary>
/// 策略基类：封装通用的 JSON 反序列化、用户提示词构建逻辑。
/// 子类只需提供系统提示词与资产类型标签。
/// </summary>
public abstract class CriteriaGenerationStrategyBase<TCriteria> : ICriteriaGenerationStrategy<TCriteria>
    where TCriteria : IScreeningCriteria
{
    private static readonly JsonSerializerOptions DeserializationOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 资产类型标签，用于用户提示词中（如"股票"/"虚拟币"）
    /// </summary>
    protected abstract string AssetTypeLabel { get; }

    public abstract MarketType SupportedMarketType { get; }

    public abstract string BuildUserRequirementSystemPrompt();

    public abstract string BuildNewsAnalysisSystemPrompt();

    public virtual string BuildUserPrompt(InvestmentSelectionWorkflowRequest request)
    {
        var label = AssetTypeLabel;
        var source = request.IsNewsAnalysis ? "新闻内容" : "用户需求";
        return $"""
            {source}：
            {request.Content}

            推荐{label}数量限制：{request.MaxRecommendations}

            请根据{source}生成{label}筛选条件。
            """;
    }

    public TCriteria DeserializeCriteria(string json)
    {
        var criteria = LlmJsonExtractor.Deserialize<TCriteria>(json, DeserializationOptions);
        if (criteria == null)
        {
            throw new InvalidOperationException($"{AssetTypeLabel}筛选条件 JSON 解析失败");
        }
        return criteria;
    }
}

using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Services.Agents.Analysts;

/// <summary>
/// 市场情绪分析师代理
/// 专注于分析市场情绪、资金流向和投资者行为
/// </summary>
/// <remarks>
/// 只挂情绪类工具：财务维度由专职的 <see cref="FinancialAnalystAgent"/> 负责。
/// 此前同时挂 <c>IFinancialTools</c> 会让本分析师重复拉取同一批财务报表，
/// 与自身四层嵌套的输出契约叠加后单次运行输入达 26 万 token，触发 200K 熔断降级。
/// </remarks>
[DisplayName("市场情绪分析师")]
[Description("整合了行为金融分析师和市场分析师的功能")]
[RequiresTools(typeof(ISentimentTools))]
public class MarketSentimentAnalystAgent(
    IChatClient chatClient,
    IList<AITool> tools,
    AnalystPromptConfig config,
    StructuredOutputMode structuredOutputMode,
    AIContextProvider[]? aiContextProviders = null) : AnalystAgentBase(
        chatClient,
        config,
        typeof(MarketSentimentAnalysisResult),
        structuredOutputMode,
        tools,
        aiContextProviders)
{
}

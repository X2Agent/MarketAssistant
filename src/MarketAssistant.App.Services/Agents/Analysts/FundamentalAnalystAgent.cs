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
/// 基本面分析师代理
/// 专注于分析公司基本面、行业地位和长期价值
/// </summary>
[DisplayName("基本面分析师")]
[Description("整合了策略分析师和股票研究分析师的功能")]
[RequiredAnalyst]
[RequiresTools(typeof(IBasicDataTools))]
public class FundamentalAnalystAgent : AnalystAgentBase
{
    public FundamentalAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptLoader promptLoader,
        StructuredOutputMode structuredOutputMode,
        AIContextProvider[]? aiContextProviders = null)
        : base(
            chatClient,
            promptLoader.GetConfig("FundamentalAnalyst"),
            typeof(FundamentalAnalysisResult),
            structuredOutputMode,
            tools,
            aiContextProviders)
    {
    }
}

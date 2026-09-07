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
/// 技术分析师代理
/// 专注于图表形态、技术指标和交易策略分析
/// </summary>
[DisplayName("技术分析师")]
[Description("专注于图表模式和技术指标分析")]
[RequiresTools(typeof(ITechnicalDataTools))]
public class TechnicalAnalystAgent : AnalystAgentBase
{
    public TechnicalAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptConfig config,
        StructuredOutputMode structuredOutputMode,
        AIContextProvider[]? aiContextProviders = null)
        : base(
            chatClient,
            config,
            typeof(TechnicalAnalysisResult),
            structuredOutputMode,
            tools,
            aiContextProviders)
    {
    }
}

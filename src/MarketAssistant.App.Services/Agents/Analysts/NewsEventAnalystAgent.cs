using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Services.Agents.Analysts;

/// <summary>
/// 新闻事件分析师代理
/// 专注于分析新闻事件、公告和突发事件对股票的影响
/// </summary>
[DisplayName("新闻事件分析师")]
[Description("专注于新闻事件对股票的影响分析")]
[RequiresTools(typeof(INewsDataTools))]
public class NewsEventAnalystAgent : AnalystAgentBase
{
    public NewsEventAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptLoader promptLoader,
        StructuredOutputMode structuredOutputMode,
        AIContextProvider[]? aiContextProviders = null)
        : base(
            chatClient,
            promptLoader.GetConfig("NewsEventAnalyst"),
            typeof(NewsEventAnalysisResult),
            structuredOutputMode,
            tools,
            aiContextProviders)
    {
    }
}

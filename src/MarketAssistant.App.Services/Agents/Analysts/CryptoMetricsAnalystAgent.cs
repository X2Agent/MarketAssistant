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
/// 项目指标分析师代理（仅虚拟币市场）
/// 聚焦市值、流动性、波动性、市场深度等加密资产专属指标，不套用财报口径
/// </summary>
[DisplayName("项目指标分析师")]
[Description("专注于加密资产的市场指标分析：市值、供应量、流动性分布与波动性")]
[RequiresTools(typeof(IFinancialTools))]
[SupportedMarkets(MarketType.Crypto)]
public class CryptoMetricsAnalystAgent : AnalystAgentBase
{
    public CryptoMetricsAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptConfig config,
        StructuredOutputMode structuredOutputMode,
        AIContextProvider[]? aiContextProviders = null)
        : base(
            chatClient,
            config,
            typeof(CryptoMetricsAnalysisResult),
            structuredOutputMode,
            tools,
            aiContextProviders)
    {
    }
}
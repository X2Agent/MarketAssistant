using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Services.Agents.Analysts;

/// <summary>
/// 财务分析师代理
/// 专注于深入分析公司财务报表和财务健康状况
/// </summary>
[DisplayName("财务分析师")]
[Description("专注于财务报表和财务健康分析")]
[RequiresTools(typeof(IFinancialTools))]
public class FinancialAnalystAgent : AnalystAgentBase
{
    public FinancialAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptLoader promptLoader,
        AIContextProvider[]? aiContextProviders = null,
        AgentSkillsProvider? skillsProvider = null)
        : base(
            chatClient,
            StructuredOutputHelper.MergeSchemaPrompt(promptLoader.GetConfig("FinancialAnalyst"), typeof(FinancialAnalysisResult)),
            ChatResponseFormat.Json,
            tools,
            aiContextProviders,
            skillsProvider)
    {
    }
}

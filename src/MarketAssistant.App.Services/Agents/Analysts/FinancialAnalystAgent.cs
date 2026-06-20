using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 财务分析师代理
/// 专注于深入分析公司财务报表和财务健康状况
/// </summary>
[DisplayName("财务分析师")]
[Description("专注于财务报表和财务健康分析")]
[RequiresTools(typeof(IFinancialTools))]
public class FinancialAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(FinancialAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(FinancialAnalysisResult),
        schemaDescription: "财务分析师的结构化分析结果，包含财务健康、盈利质量、现金流和风险预警"
    );

    public FinancialAnalystAgent(
        IChatClient chatClient,
        IFinancialTools financialTools,
        AnalystPromptLoader promptLoader,
        AIContextProvider[]? aiContextProviders = null,
        AgentSkillsProvider? skillsProvider = null)
        : base(
            chatClient,
            promptLoader.GetConfig("FinancialAnalyst"),
            ResponseFormat,
            [.. financialTools.GetFunctions()],
            aiContextProviders,
            skillsProvider)
    {
    }

}

using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 技术分析师代理
/// 专注于图表形态、技术指标和交易策略分析
/// </summary>
[DisplayName("技术分析师")]
[Description("专注于图表模式和技术指标分析")]
[RequiresTools(typeof(ITechnicalDataTools))]
public class TechnicalAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(TechnicalAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(TechnicalAnalysisResult),
        schemaDescription: "技术分析师的结构化分析结果，包含图表形态、关键价位、技术指标和交易策略"
    );

    public TechnicalAnalystAgent(
        IChatClient chatClient,
        ITechnicalDataTools technicalTools,
        AnalystPromptLoader promptLoader,
        AIContextProvider[]? aiContextProviders = null,
        AgentSkillsProvider? skillsProvider = null)
        : base(
            chatClient,
            promptLoader.GetConfig("TechnicalAnalyst"),
            ResponseFormat,
            [.. technicalTools.GetFunctions()],
            aiContextProviders,
            skillsProvider)
    {
    }
}

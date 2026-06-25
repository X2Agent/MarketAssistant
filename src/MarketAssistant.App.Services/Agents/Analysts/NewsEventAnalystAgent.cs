using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 新闻事件分析师代理
/// 专注于分析新闻事件、公告和突发事件对股票的影响
/// </summary>
[DisplayName("新闻事件分析师")]
[Description("专注于新闻事件对股票的影响分析")]
[RequiresTools(typeof(INewsDataTools))]
public class NewsEventAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(NewsEventAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(NewsEventAnalysisResult),
        schemaDescription: "新闻事件分析师的结构化分析结果，包含事件解读、影响评估和投资启示"
    );

    public NewsEventAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptLoader promptLoader,
        AIContextProvider[]? aiContextProviders = null,
        AgentSkillsProvider? skillsProvider = null)
        : base(
            chatClient,
            promptLoader.GetConfig("NewsEventAnalyst"),
            ResponseFormat,
            tools,
            aiContextProviders,
            skillsProvider)
    {
    }
}

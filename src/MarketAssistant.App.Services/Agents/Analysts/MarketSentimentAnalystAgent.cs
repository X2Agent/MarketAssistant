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
[DisplayName("市场情绪分析师")]
[Description("整合了行为金融分析师和市场分析师的功能")]
[RequiresTools(typeof(IFinancialTools))]
[RequiresTools(typeof(ISentimentTools))]
public class MarketSentimentAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(MarketSentimentAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(MarketSentimentAnalysisResult),
        schemaDescription: "市场情绪分析师的结构化分析结果，包含市场情绪、资金流向、投资者行为和短期策略"
    );

    public MarketSentimentAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        AnalystPromptLoader promptLoader,
        AIContextProvider[]? aiContextProviders = null,
        AgentSkillsProvider? skillsProvider = null)
        : base(
            chatClient,
            promptLoader.GetConfig("MarketSentimentAnalyst"),
            ResponseFormat,
            tools,
            aiContextProviders,
            skillsProvider)
    {
    }
}

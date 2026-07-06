using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.ContextProviders;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Services.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Services.Agents.Analysts;

/// <summary>
/// 协调分析师代理
/// 整合多维度分析师结论并提供投资建议
/// </summary>
[DisplayName("协调分析师")]
[Description("整合多维度分析师结论并提供投资建议")]
[RequiredAnalyst]

public class CoordinatorAnalystAgent : AnalystAgentBase
{
    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: AIJsonUtilities.CreateJsonSchema(typeof(CoordinatorResult)),
        schemaName: nameof(CoordinatorResult),
        schemaDescription: "协调分析师的综合分析结果，包含投资建议、评分、风险评估等结构化数据"
    );

    public CoordinatorAnalystAgent(
        IChatClient chatClient,
        IList<AITool> tools,
        GroundingSearchTools searchTools,
        IUserSettingService userSettingService,
        ILoggerFactory loggerFactory,
        AnalystPromptLoader promptLoader,
        AIContextProvider[]? aiContextProviders = null,
        AgentSkillsProvider? skillsProvider = null)
        : base(
            chatClient,
            promptLoader.GetConfig("CoordinatorAnalyst"),
            ResponseFormat,
            [.. tools, AIFunctionFactory.Create(searchTools.SearchAsync)],
            [
                new InvestmentPreferenceContextProvider(
                    userSettingService.CurrentSetting.InvestmentPreference,
                    loggerFactory.CreateLogger<InvestmentPreferenceContextProvider>()),
                .. (aiContextProviders ?? [])
            ],
            skillsProvider)
    {
    }
}

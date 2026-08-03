using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.ContextProviders;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Infrastructure.Providers;
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
            StructuredOutputHelper.MergeSchemaPrompt(promptLoader.GetConfig("CoordinatorAnalyst"), typeof(CoordinatorResult)),
            ChatResponseFormat.Json,
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

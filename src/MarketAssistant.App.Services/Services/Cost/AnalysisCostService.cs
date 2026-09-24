using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Agents.Analysts;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Cost;

/// <summary>
/// <see cref="IAnalysisCostService"/> 实现。模型解析口径与
/// <c>MarketAnalysisWorkflow.ResolveRuntimeForRole</c> 保持一致的降级行为：
/// 角色级分配优先，缺失回落服务商默认模型；解析不出模型时不猜价格。
/// </summary>
public sealed class AnalysisCostService : IAnalysisCostService
{
    private readonly IUserSettingService _userSettingService;
    private readonly AnalystPromptLoader _promptLoader;
    private readonly MarketContext _marketContext;
    private readonly ILogger<AnalysisCostService> _logger;

    public AnalysisCostService(
        IUserSettingService userSettingService,
        AnalystPromptLoader promptLoader,
        MarketContext marketContext,
        ILogger<AnalysisCostService> logger)
    {
        _userSettingService = userSettingService;
        _promptLoader = promptLoader;
        _marketContext = marketContext;
        _logger = logger;
    }

    public ModelCostEstimate Estimate(AnalysisTokenUsage? usage)
    {
        var setting = _userSettingService.CurrentSetting;

        return ModelCostCalculator.Estimate(
            usage,
            setting.ModelPrices,
            agentName => ResolveModelKey(setting, agentName));
    }

    public string FormatCost(AnalysisTokenUsage? usage)
        => ModelCostCalculator.FormatCost(Estimate(usage));

    /// <summary>
    /// Agent ASCII 名 → "provider|model" 键。未识别的 Agent 名解析为 null（未定价），
    /// 避免把成本记到错误模型上。
    /// </summary>
    private string? ResolveModelKey(UserSetting setting, string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return null;
        }

        var roleType = FindAnalystTypeByAgentName(agentName);
        if (roleType is null)
        {
            return null;
        }

        var providerId = setting.ProviderId;
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var modelId = AnalystModelAssignment.ResolveEffectiveModelId(setting, providerId, roleType.Name);
        return string.IsNullOrWhiteSpace(modelId) ? null : $"{providerId}|{modelId}";
    }

    /// <summary>
    /// 按 Agent ASCII 名反查分析师类型：class name → 提示词 key → YAML config.Name。
    /// </summary>
    private Type? FindAnalystTypeByAgentName(string agentName)
    {
        foreach (var type in AnalystTypeRegistry.GetConcreteAnalystTypes())
        {
            var promptKey = type.Name.EndsWith("Agent", StringComparison.Ordinal)
                ? type.Name[..^"Agent".Length]
                : type.Name;

            var config = _promptLoader.GetConfig(_marketContext.CurrentMarket, promptKey);
            if (string.Equals(config?.Name, agentName, StringComparison.Ordinal))
            {
                return type;
            }
        }

        _logger.LogDebug("成本估算未匹配到分析师角色: {AgentName}", agentName);
        return null;
    }
}

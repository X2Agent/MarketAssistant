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
public sealed class AnalysisCostService(
    IUserSettingService userSettingService,
    AnalystPromptLoader promptLoader,
    MarketContext marketContext,
    ILogger<AnalysisCostService> logger) : IAnalysisCostService
{
    private readonly IUserSettingService _userSettingService = userSettingService;
    private readonly AnalystPromptLoader _promptLoader = promptLoader;
    private readonly MarketContext _marketContext = marketContext;
    private readonly ILogger<AnalysisCostService> _logger = logger;

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

    public string FormatCostForModel(string? providerId, string? modelId, long inputTokens, long outputTokens)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId))
        {
            return string.Empty;
        }

        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return string.Empty;
        }

        // 复用同一条价格匹配与格式化逻辑，避免简报与分析报告出现两套算法。
        var usage = new AnalysisTokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CallCount = 1,
            Entries =
            [
                new TokenUsageEntry
                {
                    // 模型已知时用模型名作归集键；模型解析在下方由 resolver 直接给出
                    AgentName = modelId,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    CallCount = 1
                }
            ]
        };

        var estimate = ModelCostCalculator.Estimate(
            usage,
            _userSettingService.CurrentSetting.ModelPrices,
            _ => $"{providerId}|{modelId}");

        // 仅一个明细行时该行对应用户实际调用的模型，无需按分析师名反查
        return ModelCostCalculator.FormatCost(estimate);
    }

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

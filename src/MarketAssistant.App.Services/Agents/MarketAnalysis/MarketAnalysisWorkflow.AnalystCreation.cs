using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Agents.Analysts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// <see cref="MarketAnalysisWorkflow"/> 的分析师发现与实例化部分：
/// 依据设置中启用的角色解析出分析师类型，并为其创建 Agent 实例。
/// </summary>
/// <remarks>
/// 独立成文件的理由：分析师「有哪些、怎么创建」是独立的组装职责，
/// 与「如何编排分析」无关。创建失败会被记录并降级隔离，不中断整轮分析。
/// </remarks>
public partial class MarketAnalysisWorkflow
{
    private List<Type> GetEnabledAnalysts()
    {
        var enabledAnalysts = new List<Type>();
        var enabledAnalystRoles = _userSettingService.CurrentSetting.EnabledAnalystRoles;

        // 获取所有 AnalystAgentBase 的非抽象子类
        var agentTypes = AnalystTypeRegistry.GetConcreteAnalystTypes();
        var currentMarket = _marketContext.CurrentMarket;

        foreach (var agentType in agentTypes)
        {
            // 排除 CoordinatorAnalystAgent，它由CoordinatorExecutor独自管理
            if (agentType.Name == nameof(CoordinatorAnalystAgent)) continue;

            // 分析师必须声明支持当前市场（未标注 SupportedMarkets 视为全市场支持）
            if (!SupportedMarketsAttribute.SupportsMarket(agentType, currentMarket)) continue;

            var agentClassName = agentType.Name;

            bool isRequired = agentType.GetCustomAttribute<RequiredAnalystAttribute>() != null;

            // 确定启用状态
            bool isEnabled = false;
            if (enabledAnalystRoles.TryGetValue(agentClassName, out var userEnabled))
            {
                isEnabled = userEnabled;
            }

            // 必需的角色始终启用
            if (isRequired) isEnabled = true;

            if (isEnabled)
            {
                enabledAnalysts.Add(agentType);
            }
        }

        return enabledAnalysts;
    }

    /// <summary>
    /// 创建分析师代理（使用 Factory 模式），返回成功创建的 Agent 列表及失败的类型列表。
    /// 每位成功创建的分析师都会附加失败隔离包装，保证 Fan-In 聚合器总能收齐消息。
    /// </summary>
    private (
        List<AIAgent> Agents,
        List<Type> FailedTypes,
        IReadOnlyDictionary<string, string> NameToDisplayName) CreateAnalystAgents(
        List<Type> analystTypes,
        MarketSnapshotContextProvider marketSnapshot,
        ChatClientRuntime runtime,
        Action<Type, Exception>? onAnalystDegraded = null)
    {
        _logger.LogInformation("开始创建分析师代理，数量: {Count}", analystTypes.Count);

        var sharedProviders = new AIContextProvider[] { marketSnapshot };
        var createdAgents = new List<AIAgent>();
        var failedTypes = new List<Type>();
        var nameToDisplayName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in analystTypes)
        {
            try
            {
                // 角色级模型分配：该角色单独配置了模型时使用独立 Runtime，
                // 未配置则复用默认 Runtime（避免无谓占用 MaxCachedRuntimes 配额）
                var agentRuntime = ResolveRuntimeForRole(type, runtime);
                var agent = _analystAgentFactory.CreateAnalyst(type, agentRuntime, sharedProviders)
                    .WithFailureIsolation(exception => onAnalystDegraded?.Invoke(type, exception));
                createdAgents.Add(agent);

                // 创建时即建立 Name → DisplayName 映射。
                // agent.Name 经 DelegatingAIAgent 透传，等于 YAML 中的 ASCII config.Name
                // （如 "FundamentalAnalyst"），正是 MAF 生成 ExecutorId 时使用的前缀。
                // DisplayName 取类型上的 [DisplayName] 特性（与 YAML displayName 一致）。
                var displayName = GetAnalystDisplayNameFromType(type);
                if (!string.IsNullOrEmpty(agent.Name))
                {
                    nameToDisplayName[agent.Name] = displayName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过创建分析师代理: {AgentType}", type.Name);
                failedTypes.Add(type);
            }
        }

        _logger.LogInformation("成功创建分析师代理，实际数量: {Count}", createdAgents.Count);
        return (createdAgents, failedTypes, nameToDisplayName);
    }
}

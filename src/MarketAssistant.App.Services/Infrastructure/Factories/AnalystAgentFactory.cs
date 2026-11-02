using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.PromptConfiguration;
using Microsoft.Extensions.AI;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 分析师代理工厂接口
/// </summary>
public interface IAnalystAgentFactory
{
    /// <summary>
    /// 创建分析师代理。
    /// </summary>
    /// <param name="agentType">分析师类型。</param>
    /// <param name="runtime">聊天运行时。</param>
    /// <param name="additionalProviders">附加上下文提供者。</param>
    /// <param name="extraTools">额外追加的工具（如协调器的产物读取工具），可为 null。</param>
    /// <param name="onAnalysisFailed">
    /// 分析师运行失败的降级回调。传入时返回的 Agent 会附加失败隔离包装
    /// （内层异常 / 空输出转换为失败标记消息）；传 null 则不附加隔离（如协调器，其失败应向上传播）。
    /// </param>
    AIAgent CreateAnalyst(
        Type agentType,
        ChatClientRuntime runtime,
        AIContextProvider[]? additionalProviders = null,
        IEnumerable<AITool>? extraTools = null,
        Action<Exception>? onAnalysisFailed = null);
}

/// <summary>
/// 分析师代理工厂实现
/// 负责创建配置好的分析师代理（使用 DI 容器）
/// </summary>
public class AnalystAgentFactory(
    IServiceProvider serviceProvider,
    MarketContext marketContext,
    AgentSkillsProvider skillsProvider,
    AnalystPromptLoader promptLoader,
    TokenTrackingMiddleware tokenTracking,
    ILogger<AnalystAgentFactory> logger) : AgentFactoryBase(serviceProvider, tokenTracking, logger), IAnalystAgentFactory
{
    private readonly MarketContext _marketContext = marketContext ?? throw new ArgumentNullException(nameof(marketContext));
    private readonly AgentSkillsProvider _skillsProvider = skillsProvider ?? throw new ArgumentNullException(nameof(skillsProvider));
    private readonly AnalystPromptLoader _promptLoader = promptLoader ?? throw new ArgumentNullException(nameof(promptLoader));

    public AIAgent CreateAnalyst(
        Type agentType,
        ChatClientRuntime runtime,
        AIContextProvider[]? additionalProviders = null,
        IEnumerable<AITool>? extraTools = null,
        Action<Exception>? onAnalysisFailed = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(runtime);

            // 严格限制必须是 AnalystAgentBase 的子类
            if (!typeof(AnalystAgentBase).IsAssignableFrom(agentType))
            {
                throw new ArgumentException($"Type {agentType.Name} must inherit from AnalystAgentBase", nameof(agentType));
            }

            // 根据当前市场类型获取对应的工具实现
            var currentMarket = _marketContext.CurrentMarket;
            var tools = ResolveToolsFor(agentType, currentMarket);

            // 追加调用方传入的额外工具（如协调器的产物读取工具）
            if (extraTools != null)
            {
                tools = [.. tools, .. extraTools];
            }

            // 按类型名约定解析提示词配置（TechnicalAnalystAgent → TechnicalAnalyst），优先市场覆写文件
            var promptKey = agentType.Name.EndsWith("Agent", StringComparison.Ordinal)
                ? agentType.Name[..^"Agent".Length]
                : agentType.Name;
            var promptConfig = _promptLoader.GetConfig(currentMarket, promptKey);

            // 显式传递 Runtime Client、结构化输出模式、工具和上下文，其余由 DI 自动解析。
            var parameters = new List<object>
            {
                runtime.Client,
                tools,
                runtime.StructuredOutputMode,
                promptConfig
            };

            Logger.LogInformation(
                "创建分析师代理: {AgentType}, 市场: {Market}, Provider: {ProviderId}, Model: {ModelId}, ResponseFormat: {StructuredOutputMode}, Tools: {ToolCount}",
                agentType.Name,
                currentMarket,
                runtime.ProviderId,
                runtime.ModelId,
                runtime.StructuredOutputMode,
                tools.Count);

            AIContextProvider[] contextProviders =
            [
                _skillsProvider,
                .. (additionalProviders ?? [])
            ];
            parameters.Add(contextProviders);

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(ServiceProvider, agentType, [.. parameters]);

            // 中间件组合顺序的唯一入口（顺序契约）：
            // ChatClientAgent → TokenTracking → FailureIsolation（最外层）。
            // 失败隔离必须在 TokenTracking 的外层：Token 熔断等内层抛出的异常才能在此
            // 被转换为失败标记消息（Fan-In 聚合器仍能收到该分析师的消息，降级继续），
            // 而不是使整个分析工作流中断。新增中间件时保持失败隔离最外层不变。
            var middlewareAgent = WrapWithTokenTracking(agent);
            var finalAgent = onAnalysisFailed is null
                ? middlewareAgent
                : middlewareAgent.WithFailureIsolation(onAnalysisFailed);

            Logger.LogInformation(
                "成功创建分析师代理: {AgentType} (市场: {Market}，已附加中间件，失败隔离: {HasFailureIsolation})",
                agentType.Name, currentMarket, onAnalysisFailed is not null);

            return finalAgent;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "创建分析师代理时发生错误: {AgentType}", agentType.Name);
            throw;
        }
    }
}

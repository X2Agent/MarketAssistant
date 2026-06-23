using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
using MarketAssistant.Trading;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场分析并发工作流（基于 Agent Framework 最佳实践）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/agents-in-workflows
/// </summary>
public class MarketAnalysisWorkflow
{
    private readonly AnalysisAggregatorExecutor _aggregatorExecutor;
    private readonly CoordinatorExecutor _coordinatorExecutor;
    private readonly IUserSettingService _userSettingService;
    private readonly IAnalystAgentFactory _analystAgentFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MarketAnalysisWorkflow> _logger;
    private readonly AnalysisReportCache _reportCache;

    /// <summary>
    /// MAF Agent <c>Name</c>（ASCII 标识符，如 "FundamentalAnalyst"）→ 中文显示名（如"基本面分析师"）映射。
    /// 在 <see cref="CreateAnalystAgents"/> 创建 Agent 时一并建立，供
    /// <see cref="GetDisplayNameForExecutorId"/> 把工作流 ExecutorId（如
    /// <c>FundamentalAnalyst_826faad2...</c>）翻译为用户可读的显示名。
    /// </summary>
    /// <remarks>
    /// 为何不能直接把中文 DisplayName 作为 MAF Agent Name：MAF 的
    /// <c>AIAgentExtensions.GetDescriptiveId</c> 会用正则 <c>[^0-9A-Za-z]+</c> 清洗
    /// <c>Name + "_" + Id</c> 生成 ExecutorId，中文字符会被整体替换为单个下划线，
    /// 导致 ExecutorId 退化为 <c>_826faad2...</c>。故 Name 必须为 ASCII，
    /// 显示名在本映射中维护。
    /// </remarks>
    private readonly Dictionary<string, string> _agentNameToDisplayName = new();

    /// <summary>
    /// 分析进度事件
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs>? ProgressChanged;

    public MarketAnalysisWorkflow(
        AnalysisAggregatorExecutor aggregatorExecutor,
        CoordinatorExecutor coordinatorExecutor,
        IUserSettingService userSettingService,
        IAnalystAgentFactory analystAgentFactory,
        ILoggerFactory loggerFactory,
        AnalysisReportCache reportCache,
        ILogger<MarketAnalysisWorkflow> logger)
    {
        _aggregatorExecutor = aggregatorExecutor ?? throw new ArgumentNullException(nameof(aggregatorExecutor));
        _coordinatorExecutor = coordinatorExecutor ?? throw new ArgumentNullException(nameof(coordinatorExecutor));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _analystAgentFactory = analystAgentFactory ?? throw new ArgumentNullException(nameof(analystAgentFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _reportCache = reportCache ?? throw new ArgumentNullException(nameof(reportCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行市场分析工作流
    /// </summary>
    public async Task<MarketAnalysisReport> AnalyzeAsync(
        string assetSymbol,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始执行市场分析工作流，标的代码: {AssetSymbol}", assetSymbol);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                StageDescription = "正在准备分析环境",
                IsInProgress = true
            });

            // 获取启用的分析师列表
            var enabledAnalysts = GetEnabledAnalysts();
            if (enabledAnalysts.Count == 0)
            {
                throw new InvalidOperationException("没有启用任何分析师，请在设置中至少启用一位分析师");
            }

            // 创建分析师代理（记录失败的分析师用于降级提示）
            // 每次分析创建独立的市场快照实例，避免 Singleton 共享可变状态导致并发数据竞争
            var marketSnapshot = new MarketSnapshotContextProvider();
            marketSnapshot.SetData("分析标的", assetSymbol);
            marketSnapshot.SetData("分析时间", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));

            var analystAgents = CreateAnalystAgents(enabledAnalysts, marketSnapshot);
            var failedAnalystNames = analystAgents.FailedTypes
                .Select(GetAnalystDisplayNameFromType)
                .ToList();
            var createdAgents = analystAgents.Agents;

            if (createdAgents.Count == 0)
            {
                throw new InvalidOperationException("所有分析师创建失败，无法执行分析");
            }

            // 构建工作流（传入分析师数量）
            var workflow = BuildWorkflow(createdAgents.Count, createdAgents);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                StageDescription = $"{createdAgents.Count} 位分析师正在并发分析",
                IsInProgress = true,
                TotalAnalysts = createdAgents.Count,
                FailedAnalysts = failedAnalystNames
            });

            // 执行工作流（流式处理）
            var finalReport = await ExecuteWorkflowAsync(workflow, assetSymbol, createdAgents.Count, cancellationToken);

            // 缓存分析结果，供交易决策模块使用
            _reportCache.Set(assetSymbol, finalReport);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                StageDescription = "分析完成",
                IsInProgress = false
            });

            return finalReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行市场分析工作流时发生错误");
            OnProgressChanged(new AnalysisProgressEventArgs
            {
                StageDescription = $"分析失败: {ex.Message}",
                IsInProgress = false
            });
            throw;
        }
    }

    /// <summary>
    /// 执行工作流并处理事件
    /// </summary>
    private async Task<MarketAnalysisReport> ExecuteWorkflowAsync(
        Workflow workflow,
        string assetSymbol,
        int analystCount,
        CancellationToken cancellationToken)
    {
        MarketAnalysisReport? finalReport = null;
        bool reportReceived = false;
        int completedAnalysts = 0;
        int totalAnalysts = analystCount;
        var failedSteps = new List<string>();

        // 执行工作流（流式处理）
        // 初始输入 assetSymbol 会触发 Dispatcher，Dispatcher 再通过 context.SendMessageAsync
        // 广播 ChatMessage 和 TurnToken 给所有分析师，触发其 LLM 调用
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow,
            assetSymbol,
            checkpointManager: null,
            sessionId: null,
            cancellationToken);

        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ExecutorInvokedEvent executorInvoked:
                    _logger.LogDebug("工作流步骤开始: {ExecutorId}", executorInvoked.ExecutorId);

                    string stageName = GetExecutorNamePrefix(executorInvoked.ExecutorId) switch
                    {
                        "AnalysisDispatcher" => "正在分发分析任务",
                        "AnalysisAggregator" => "正在聚合分析结果",
                        "Coordinator" => "正在生成综合报告",
                        _ => $"{GetDisplayNameForExecutorId(executorInvoked.ExecutorId)} 正在分析"
                    };

                    OnProgressChanged(new AnalysisProgressEventArgs
                    {
                        StageDescription = stageName,
                        IsInProgress = true,
                        TotalAnalysts = totalAnalysts,
                        CompletedAnalysts = completedAnalysts
                    });
                    break;

                case ExecutorCompletedEvent executorComplete:
                    _logger.LogDebug("工作流步骤完成: {ExecutorId}", executorComplete.ExecutorId);

                    if (IsAnalystExecutor(executorComplete.ExecutorId))
                    {
                        completedAnalysts++;
                        OnProgressChanged(new AnalysisProgressEventArgs
                        {
                            StageDescription = $"{GetDisplayNameForExecutorId(executorComplete.ExecutorId)} 分析完成",
                            IsInProgress = true,
                            TotalAnalysts = totalAnalysts,
                            CompletedAnalysts = completedAnalysts,
                            CompletedAnalystName = executorComplete.ExecutorId
                        });
                    }
                    break;

                case WorkflowOutputEvent workflowOutput:
                    if (!reportReceived)
                    {
                        finalReport = workflowOutput.Data as MarketAnalysisReport;
                        reportReceived = true;
                        _logger.LogInformation("工作流完成，生成最终报告");
                    }
                    break;

                case ExecutorFailedEvent executorFailed:
                    var errorMessage = executorFailed.Data?.Message ?? "未知错误";
                    _logger.LogError(executorFailed.Data,
                        "步骤失败: {ExecutorId}, 错误: {Error}",
                        executorFailed.ExecutorId,
                        errorMessage);
                    failedSteps.Add(GetDisplayNameForExecutorId(executorFailed.ExecutorId));

                    // 关键步骤失败则抛出，非关键步骤记录并继续
                    if (IsSystemExecutor(executorFailed.ExecutorId))
                    {
                        throw new FriendlyException(errorMessage);
                    }

                    OnProgressChanged(new AnalysisProgressEventArgs
                    {
                        StageDescription = $"{GetDisplayNameForExecutorId(executorFailed.ExecutorId)} 分析失败，继续其他分析",
                        IsInProgress = true,
                        TotalAnalysts = totalAnalysts,
                        CompletedAnalysts = completedAnalysts,
                        FailedAnalysts = failedSteps
                    });
                    break;

                case WorkflowErrorEvent workflowError:
                    var wfErrorMsg = workflowError.Exception?.Message ?? "市场分析工作流内部发生未知错误";
                    _logger.LogError(workflowError.Exception,
                        "市场分析工作流发生严重错误: {Message}", wfErrorMsg);
                    throw new FriendlyException(wfErrorMsg);

                case SuperStepCompletedEvent superStepCompleted:
                    _logger.LogDebug("工作流 SuperStep 完成");
                    break;

                case WorkflowWarningEvent workflowWarning:
                    _logger.LogWarning("市场分析工作流警告: {Warning}", workflowWarning.Data);
                    break;
            }
        }

        return finalReport ?? throw new FriendlyException("工作流未返回分析报告");
    }

    /// <summary>
    /// 获取启用的分析师列表
    /// </summary>
    private List<Type> GetEnabledAnalysts()
    {
        var enabledAnalysts = new List<Type>();
        var enabledAnalystRoles = _userSettingService.CurrentSetting.EnabledAnalystRoles;

        // 获取所有 AnalystAgentBase 的非抽象子类
        var agentTypes = AnalystTypeRegistry.GetConcreteAnalystTypes();

        foreach (var agentType in agentTypes)
        {
            // 排除 CoordinatorAnalystAgent，它由CoordinatorExecutor独自管理
            if (agentType.Name == nameof(CoordinatorAnalystAgent)) continue;

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
    /// 创建分析师代理（使用 Factory 模式），返回成功创建的 Agent 列表及失败的类型列表
    /// </summary>
    private (List<AIAgent> Agents, List<Type> FailedTypes) CreateAnalystAgents(
        List<Type> analystTypes,
        MarketSnapshotContextProvider marketSnapshot)
    {
        _logger.LogInformation("开始创建分析师代理，数量: {Count}", analystTypes.Count);

        var sharedProviders = new AIContextProvider[] { marketSnapshot };
        var createdAgents = new List<AIAgent>();
        var failedTypes = new List<Type>();

        foreach (var type in analystTypes)
        {
            try
            {
                var agent = _analystAgentFactory.CreateAnalyst(type, sharedProviders);
                createdAgents.Add(agent);

                // 创建时即建立 Name → DisplayName 映射。
                // agent.Name 经 DelegatingAIAgent 透传，等于 YAML 中的 ASCII config.Name
                // （如 "FundamentalAnalyst"），正是 MAF 生成 ExecutorId 时使用的前缀。
                // DisplayName 取类型上的 [DisplayName] 特性（与 YAML displayName 一致）。
                var displayName = GetAnalystDisplayNameFromType(type);
                if (!string.IsNullOrEmpty(agent.Name))
                {
                    _agentNameToDisplayName[agent.Name] = displayName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过创建分析师代理: {AgentType}", type.Name);
                failedTypes.Add(type);
            }
        }

        _logger.LogInformation("成功创建分析师代理，实际数量: {Count}", createdAgents.Count);
        return (createdAgents, failedTypes);
    }

    /// <summary>
    /// 根据分析师类型获取显示名称（用于创建失败时的降级提示）。
    /// 优先读取 <see cref="DisplayNameAttribute"/>，回退到类型名。
    /// </summary>
    private static string GetAnalystDisplayNameFromType(Type analystType)
    {
        return analystType.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
            ?? analystType.Name;
    }

    /// <summary>
    /// 从 ExecutorId 中提取 Name 前缀（第一个下划线之前的部分）。
    /// <para>
    /// MAF 的 <c>AIAgentExtensions.GetDescriptiveId</c> 生成 ExecutorId 的方式为
    /// <c>Regex.Replace(Name + "_" + Id, "[^0-9A-Za-z]+", "_")</c>。由于 <c>Name</c>
    /// 为 ASCII 标识符、<c>Id</c> 为 GUID 的 "N" 格式（纯十六进制），二者均不含下划线，
    /// 因此 ExecutorId 中有且仅有一个下划线作为分隔符。
    /// </para>
    /// </summary>
    private static string GetExecutorNamePrefix(string executorId)
    {
        var separatorIndex = executorId.IndexOf('_');
        return separatorIndex > 0 ? executorId[..separatorIndex] : executorId;
    }

    /// <summary>
    /// 判断是否为系统 Executor（Dispatcher / Aggregator / Coordinator），
    /// 即非分析师的业务执行器。
    /// </summary>
    private static bool IsSystemExecutor(string executorId)
    {
        var prefix = GetExecutorNamePrefix(executorId);
        return prefix is "AnalysisDispatcher" or "AnalysisAggregator" or "Coordinator";
    }

    /// <summary>
    /// 判断是否为分析师 Executor（排除系统 Executor）。
    /// </summary>
    private static bool IsAnalystExecutor(string executorId) => !IsSystemExecutor(executorId);

    /// <summary>
    /// 判断是否为 Dispatcher Executor。
    /// </summary>
    private static bool IsDispatcherExecutor(string executorId)
        => GetExecutorNamePrefix(executorId) == "AnalysisDispatcher";

    /// <summary>
    /// 从工作流 ExecutorId 中提取分析师显示名称。
    /// 按第一个下划线切出 Name 前缀，再在 <see cref="_agentNameToDisplayName"/> 中查中文显示名。
    /// </summary>
    private string GetDisplayNameForExecutorId(string executorId)
    {
        var namePrefix = GetExecutorNamePrefix(executorId);

        return _agentNameToDisplayName.TryGetValue(namePrefix, out var displayName)
            ? displayName
            : executorId;
    }

    /// <summary>
    /// 构建并发工作流（使用框架原生并发编排）
    /// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow
    /// 
    /// 流程：
    /// [Dispatcher] → [并发分析师团队] → [Aggregator] → [Coordinator]
    /// </summary>
    private Workflow BuildWorkflow(int analystCount, List<AIAgent> analystAgents)
    {
        // 构建标准 Fan-Out/Fan-In 工作流：
        // 
        // [Dispatcher] string (assetSymbol) → broadcast ChatMessage
        //      ↓ (Fan-Out)
        // [Analyst1] [Analyst2] [Analyst3] ... (并发执行，每个返回 ChatMessage)
        //      ↓ ↓ ↓ ↓ (Fan-In: 框架逐个传递给 Aggregator)
        // [Aggregator] 收集所有 ChatMessage → List<ChatMessage>
        //      ↓
        // [Coordinator] List<ChatMessage> → MarketAnalysisReport (输出)

        // 1. 动态创建 Dispatcher（需要知道分析师数量）
        var dispatcher = new AnalysisDispatcherExecutor(
            analystCount,
            _loggerFactory.CreateLogger<AnalysisDispatcherExecutor>());

        // 2. 创建工作流，Dispatcher 作为入口节点
        var builder = new WorkflowBuilder(dispatcher);

        // 3. Fan-Out: Dispatcher → 所有分析师（Dispatcher 广播 ChatMessage）
        // AIAgent 可以直接用于工作流，框架会自动处理
        builder.AddFanOutEdge(dispatcher, [.. analystAgents]);

        // 4. Fan-In: 所有分析师 → Aggregator
        // 框架会自动收集所有源（分析师）的消息，并作为 List<ChatMessage> 一次性传递给 Aggregator
        builder.AddFanInBarrierEdge([.. analystAgents], _aggregatorExecutor);

        // 5. Aggregator → Coordinator（将聚合结果传递给协调分析师）
        builder.AddEdge(_aggregatorExecutor, _coordinatorExecutor);

        // 6. 设置输出来自 Coordinator
        builder.WithOutputFrom(_coordinatorExecutor);

        return builder.Build();
    }

    /// <summary>
    /// 触发进度事件
    /// </summary>
    protected virtual void OnProgressChanged(AnalysisProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }
}

/// <summary>
/// 分析进度变化事件参数
/// </summary>
public sealed class AnalysisProgressEventArgs : EventArgs
{
    /// <summary>
    /// 当前阶段描述
    /// </summary>
    public string StageDescription { get; set; } = string.Empty;

    /// <summary>
    /// 是否正在进行中
    /// </summary>
    public bool IsInProgress { get; set; } = true;

    /// <summary>
    /// 总分析师数量
    /// </summary>
    public int TotalAnalysts { get; set; }

    /// <summary>
    /// 已完成的分析师数量
    /// </summary>
    public int CompletedAnalysts { get; set; }

    /// <summary>
    /// 当前完成的分析师名称（如有）
    /// </summary>
    public string? CompletedAnalystName { get; set; }

    /// <summary>
    /// 失败的分析师名称列表
    /// </summary>
    public List<string> FailedAnalysts { get; set; } = [];

    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public int ProgressPercent
    {
        get
        {
            // 分析已完成或失败
            if (!IsInProgress) return 100;
            // 正在进行分析
            if (TotalAnalysts > 0)
                return Math.Min(100, (int)((double)CompletedAnalysts / TotalAnalysts * 100));
            // 准备阶段（未开始分析）
            return 0;
        }
    }
}
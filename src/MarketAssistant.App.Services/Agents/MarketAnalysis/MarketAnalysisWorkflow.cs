using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Agents.Analysts;
using MarketAssistant.Services.Agents.MarketAnalysis.Artifacts;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场分析并发工作流（基于 Agent Framework 最佳实践）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/agents-in-workflows
/// </summary>
public class MarketAnalysisWorkflow
{
    private readonly IUserSettingService _userSettingService;
    private readonly IAnalystAgentFactory _analystAgentFactory;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MarketAnalysisWorkflow> _logger;
    private readonly AnalysisReportCache _reportCache;
    private readonly MarketContext _marketContext;
    private readonly IAnalystArtifactStore _artifactStore;

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
    /// <summary>
    /// 分析进度事件
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs>? ProgressChanged;

    public MarketAnalysisWorkflow(
        IUserSettingService userSettingService,
        IAnalystAgentFactory analystAgentFactory,
        IChatClientFactory chatClientFactory,
        ILoggerFactory loggerFactory,
        AnalysisReportCache reportCache,
        MarketContext marketContext,
        IAnalystArtifactStore artifactStore,
        ILogger<MarketAnalysisWorkflow> logger)
    {
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _analystAgentFactory = analystAgentFactory ?? throw new ArgumentNullException(nameof(analystAgentFactory));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _marketContext = marketContext ?? throw new ArgumentNullException(nameof(marketContext));
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _reportCache = reportCache ?? throw new ArgumentNullException(nameof(reportCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行市场分析工作流
    /// </summary>
    public Task<MarketAnalysisReport> AnalyzeAsync(
        string assetSymbol,
        CancellationToken cancellationToken = default)
    {
        return AnalyzeAsync(assetSymbol, Guid.NewGuid(), cancellationToken);
    }

    /// <summary>
    /// 使用调用方分配的 Run ID 执行市场分析，确保并发进度事件可准确归属。
    /// </summary>
    public async Task<MarketAnalysisReport> AnalyzeAsync(
        string assetSymbol,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run ID 不能为空", nameof(runId));

        using var activity = MarketAssistantDiagnostics.StartActivity("market_analysis.workflow.run");
        activity?.SetTag("marketassistant.run.id", runId.ToString("N"));
        activity?.SetTag("marketassistant.asset.symbol", assetSymbol);

        try
        {
            _logger.LogInformation("开始执行市场分析工作流，标的代码: {AssetSymbol}", assetSymbol);

            OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
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

            var runtime = _chatClientFactory.CreateRuntime();
            activity?.SetTag("gen_ai.provider.name", runtime.ProviderId);
            activity?.SetTag("gen_ai.request.model", runtime.ModelId);
            activity?.SetTag("marketassistant.analyst.requested_count", enabledAnalysts.Count);

            // 运行期降级记录：失败隔离包装器在分析师运行失败时回调，
            // 这里维护已降级名单并向 UI 发布进度事件（与聚合器的失败计数相互独立）
            var degradedAnalysts = new List<string>();
            var degradationGate = new object();
            var totalForProgress = 0;

            var analystAgents = CreateAnalystAgents(
                enabledAnalysts,
                marketSnapshot,
                runtime,
                onAnalystDegraded: (analystType, exception) =>
                {
                    var displayName = GetAnalystDisplayNameFromType(analystType);
                    List<string> failedSnapshot;
                    lock (degradationGate)
                    {
                        if (!degradedAnalysts.Contains(displayName))
                            degradedAnalysts.Add(displayName);
                        failedSnapshot = [.. degradedAnalysts];
                    }

                    _logger.LogWarning(exception, "分析师执行失败，降级继续其余分析师: {Analyst}", displayName);
                    OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
                    {
                        StageDescription = $"{displayName} 分析失败，继续其他分析",
                        IsInProgress = true,
                        TotalAnalysts = totalForProgress,
                        FailedAnalysts = failedSnapshot
                    });
                });
            var failedAnalystNames = analystAgents.FailedTypes
                .Select(GetAnalystDisplayNameFromType)
                .ToList();
            var createdAgents = analystAgents.Agents;
            totalForProgress = createdAgents.Count;

            if (createdAgents.Count == 0)
            {
                throw new InvalidOperationException("所有分析师创建失败，无法执行分析");
            }

            // 同一次 Run 的分析师与 Coordinator 绑定同一个 Runtime Client。
            // P1-07：协调器附带只读产物读取工具，全文按需获取而非随消息注入。
            // Run ID 通过闭包捕获真实值，不暴露给 LLM 填写（LLM 传入伪造/格式错误的
            // runId 会解析失败），工具仅暴露 analystName 参数。
            var currentRunId = runId;
            var getArtifactTool = AIFunctionFactory.Create(
                ([Description("分析师名称")] string analystName,
                  CancellationToken ct)
                    => _artifactStore.GetAsync(currentRunId, analystName, ct),
                name: "get_analyst_artifact",
                description: "读取本次运行中某位分析师的完整分析产物全文。仅在需要某维度细节时调用，不要凭摘要编造内容。");

            var coordinatorAgent = _analystAgentFactory.CreateAnalyst(
                typeof(CoordinatorAnalystAgent),
                runtime,
                extraTools: [getArtifactTool]);
            var coordinatorExecutor = new CoordinatorExecutor(
                coordinatorAgent,
                _loggerFactory.CreateLogger<CoordinatorExecutor>());
            var aggregatorExecutor = new AnalysisAggregatorExecutor(
                createdAgents.Count,
                runId,
                _artifactStore,
                analystAgents.NameToDisplayName,
                _loggerFactory.CreateLogger<AnalysisAggregatorExecutor>());

            // 构建工作流（所有 Executor 均为 Run 局部实例）
            var agentNameToDisplayName = analystAgents.NameToDisplayName;
            var workflow = BuildWorkflow(
                createdAgents,
                aggregatorExecutor,
                coordinatorExecutor);

            OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
            {
                StageDescription = $"{createdAgents.Count} 位分析师正在并发分析",
                IsInProgress = true,
                TotalAnalysts = createdAgents.Count,
                FailedAnalysts = failedAnalystNames
            });

            // 执行工作流（流式处理）
            var finalReport = await ExecuteWorkflowAsync(
                workflow,
                runId,
                assetSymbol,
                createdAgents.Count,
                agentNameToDisplayName,
                degradedAnalysts,
                cancellationToken);

            // 缓存分析结果，供交易决策模块使用
            _reportCache.Set(assetSymbol, finalReport);
            activity?.SetTag("marketassistant.analyst.completed_count", createdAgents.Count);
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);

            OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
            {
                StageDescription = "分析完成",
                IsInProgress = false
            });

            return finalReport;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "cancelled");
            activity?.SetTag("error.type", ex.GetType().FullName);
            throw;
        }
        catch (Exception ex)
        {
            MarketAssistantDiagnostics.RecordException(activity, ex);
            _logger.LogError(ex, "执行市场分析工作流时发生错误");
            OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
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
        Guid runId,
        string assetSymbol,
        int analystCount,
        IReadOnlyDictionary<string, string> agentNameToDisplayName,
        List<string> degradedAnalysts,
        CancellationToken cancellationToken)
    {
        MarketAnalysisReport? finalReport = null;
        bool reportReceived = false;
        int completedAnalysts = 0;
        int totalAnalysts = analystCount;
        var failedSteps = new List<(string DisplayName, string ErrorMessage)>();
        string? lastCompletedStep = null;
        // 追踪当前正在运行的 Executor，超时时用于定位卡住的分析师
        var activeExecutors = new HashSet<string>();
        var executorStartedAt = new Dictionary<string, long>(StringComparer.Ordinal);

        // 执行工作流（流式处理）
        // 初始输入 assetSymbol 会触发 Dispatcher，Dispatcher 再通过 context.SendMessageAsync
        // 广播 ChatMessage 和 TurnToken 给所有分析师，触发其 LLM 调用
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow,
            assetSymbol,
            cancellationToken: cancellationToken);

        try
        {
            await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case ExecutorInvokedEvent executorInvoked:
                        activeExecutors.Add(executorInvoked.ExecutorId);
                        executorStartedAt[executorInvoked.ExecutorId] = Stopwatch.GetTimestamp();
                        _logger.LogInformation(
                            "工作流步骤开始: {ExecutorId}, 输入类型: {InputType}",
                            executorInvoked.ExecutorId,
                            executorInvoked.Data?.GetType().FullName ?? "null");

                        string stageName = GetExecutorNamePrefix(executorInvoked.ExecutorId) switch
                        {
                            "AnalysisDispatcher" => "正在分发分析任务",
                            "AnalysisAggregator" => "正在聚合分析结果",
                            "Coordinator" => "正在生成综合报告",
                            _ => $"{GetDisplayNameForExecutorId(executorInvoked.ExecutorId, agentNameToDisplayName)} 正在分析"
                        };

                        OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
                        {
                            StageDescription = stageName,
                            IsInProgress = true,
                            TotalAnalysts = totalAnalysts,
                            CompletedAnalysts = completedAnalysts
                        });
                        break;

                    case ExecutorCompletedEvent executorComplete:
                        activeExecutors.Remove(executorComplete.ExecutorId);
                        lastCompletedStep = GetDisplayNameForExecutorId(executorComplete.ExecutorId, agentNameToDisplayName);
                        var elapsed = executorStartedAt.Remove(executorComplete.ExecutorId, out var startedAt)
                            ? Stopwatch.GetElapsedTime(startedAt)
                            : TimeSpan.Zero;
                        _logger.LogInformation(
                            "工作流步骤完成: {ExecutorId}, 耗时: {ElapsedMs} ms, 结果类型: {ResultType}",
                            executorComplete.ExecutorId,
                            elapsed.TotalMilliseconds,
                            executorComplete.Data?.GetType().FullName ?? "null");

                        if (IsAnalystExecutor(executorComplete.ExecutorId))
                        {
                            completedAnalysts++;
                            OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
                            {
                                StageDescription = $"{lastCompletedStep} 分析完成",
                                IsInProgress = true,
                                TotalAnalysts = totalAnalysts,
                                CompletedAnalysts = completedAnalysts,
                                CompletedAnalystName = executorComplete.ExecutorId
                            });
                        }
                        break;

                    // AgentResponseUpdateEvent 继承自 WorkflowOutputEvent，必须在其之前匹配。
                    // 分析师在 TurnToken(emitEvents: true) 模式下，每个 AgentResponseUpdate 都会
                    // 通过 YieldOutputAsync 产生 AgentResponseUpdateEvent，这些是中间流式更新，
                    // 不是工作流的最终输出，应忽略。
                    case AgentResponseUpdateEvent:
                        _logger.LogDebug("收到分析师流式更新，忽略（非最终输出）");
                        break;

                    case WorkflowOutputEvent workflowOutput:
                        if (!reportReceived)
                        {
                            finalReport = workflowOutput.Data as MarketAnalysisReport;
                            if (finalReport != null)
                            {
                                reportReceived = true;
                                _logger.LogInformation("工作流完成，生成最终报告");
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "收到 WorkflowOutputEvent 但数据类型不匹配，期望 MarketAnalysisReport，实际: {ActualType}",
                                    workflowOutput.Data?.GetType().FullName ?? "null");
                            }
                        }
                        break;

                    case ExecutorFailedEvent executorFailed:
                        activeExecutors.Remove(executorFailed.ExecutorId);
                        var failedDisplayName = GetDisplayNameForExecutorId(executorFailed.ExecutorId, agentNameToDisplayName);
                        var errorDetail = executorFailed.Data?.Message ?? "未知错误";
                        _logger.LogError(executorFailed.Data,
                            "步骤失败: {ExecutorId} ({DisplayName}), 错误: {Error}",
                            executorFailed.ExecutorId, failedDisplayName, errorDetail);
                        failedSteps.Add((failedDisplayName, errorDetail));

                        if (IsSystemExecutor(executorFailed.ExecutorId))
                        {
                            throw new FriendlyException(
                                $"分析流程关键环节「{failedDisplayName}」执行失败: {errorDetail}");
                        }

                        OnProgressChanged(runId, assetSymbol, new AnalysisProgressEventArgs
                        {
                            StageDescription = $"{failedDisplayName} 分析失败，继续其他分析",
                            IsInProgress = true,
                            TotalAnalysts = totalAnalysts,
                            CompletedAnalysts = completedAnalysts,
                            FailedAnalysts = failedSteps.Select(f => f.DisplayName).ToList()
                        });
                        break;

                    case WorkflowErrorEvent workflowError:
                        var wfErrorMsg = workflowError.Exception?.Message ?? "市场分析工作流内部发生未知错误";
                        _logger.LogError(workflowError.Exception,
                            "市场分析工作流发生严重错误: {Message}", wfErrorMsg);
                        throw new FriendlyException(
                            BuildWorkflowErrorMessage(wfErrorMsg, failedSteps));

                    case SuperStepCompletedEvent superStepCompleted:
                        _logger.LogDebug("工作流 SuperStep 完成");
                        break;

                    case WorkflowWarningEvent workflowWarning:
                        _logger.LogWarning("市场分析工作流警告: {Warning}", workflowWarning.Data);
                        break;

                    default:
                        _logger.LogDebug("收到未处理的工作流事件: {EventType}", evt.GetType().Name);
                        break;
                }
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // AI 模型 API 响应超时（NetworkTimeout），精确定位卡住的分析师
            var stuckAnalysts = activeExecutors
                .Where(id => IsAnalystExecutor(id))
                .Select(id => GetDisplayNameForExecutorId(id, agentNameToDisplayName))
                .ToList();
            var stuckSystem = activeExecutors
                .Where(id => IsSystemExecutor(id))
                .Select(id => GetDisplayNameForExecutorId(id, agentNameToDisplayName))
                .ToList();

            var allStuck = stuckAnalysts.Concat(stuckSystem).ToList();
            var stuckDescription = allStuck.Count > 0
                ? string.Join("、", allStuck)
                : "未知环节";

            _logger.LogError(ex,
                "AI 模型响应超时，卡在: [{StuckExecutors}]，标的: {AssetSymbol}，已完成: {Completed}/{Total}",
                stuckDescription, assetSymbol, completedAnalysts, totalAnalysts);

            throw new FriendlyException(
                $"「{stuckDescription}」调用 AI 模型时超时无响应，请检查模型服务是否正常或尝试更换模型");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        if (finalReport != null)
        {
            return finalReport;
        }

        // 全部分析师被失败隔离包装器降级：聚合器不会派发 Coordinator，事件流自然结束。
        // Fan-In barrier 目标执行器的异常不会以 ExecutorFailedEvent 暴露，因此由这里终局判定。
        if (degradedAnalysts.Count >= analystCount && analystCount > 0)
        {
            _logger.LogError(
                "全部分析师均执行失败，标的: {AssetSymbol}，失败名单: [{DegradedAnalysts}]",
                assetSymbol,
                string.Join(", ", degradedAnalysts));

            throw new FriendlyException(
                $"所有分析师均执行失败，无法生成综合报告: {string.Join("；", degradedAnalysts)}");
        }

        // 工作流正常结束但未收到 WorkflowOutputEvent，构建详细诊断信息
        _logger.LogError(
            "工作流事件流已结束但未收到 WorkflowOutputEvent，标的: {AssetSymbol}，已完成分析师: {Completed}/{Total}，失败步骤: [{FailedSteps}]，最后完成步骤: {LastStep}",
            assetSymbol, completedAnalysts, totalAnalysts,
            string.Join(", ", failedSteps.Select(f => f.DisplayName)),
            lastCompletedStep ?? "无");

        throw new FriendlyException(
            BuildOutputMissingErrorMessage(assetSymbol, failedSteps, completedAnalysts, totalAnalysts));
    }

    /// <summary>
    /// 构建工作流错误的用户可读消息
    /// </summary>
    private static string BuildWorkflowErrorMessage(
        string baseMessage,
        List<(string DisplayName, string ErrorMessage)> failedSteps)
    {
        if (failedSteps.Count == 0)
        {
            return $"分析过程发生内部错误: {baseMessage}";
        }

        var failedDetails = string.Join("；",
            failedSteps.Select(f => $"「{f.DisplayName}」({f.ErrorMessage})"));
        return $"分析过程中以下环节出现问题: {failedDetails}。错误详情: {baseMessage}";
    }

    /// <summary>
    /// 构建工作流未输出报告时的用户可读消息
    /// </summary>
    private static string BuildOutputMissingErrorMessage(
        string assetSymbol,
        List<(string DisplayName, string ErrorMessage)> failedSteps,
        int completedAnalysts,
        int totalAnalysts)
    {
        if (failedSteps.Count > 0)
        {
            var failedDetails = string.Join("；",
                failedSteps.Select(f => $"「{f.DisplayName}」({f.ErrorMessage})"));
            return $"分析 {assetSymbol} 时部分环节失败导致无法生成报告: {failedDetails}";
        }

        if (completedAnalysts < totalAnalysts)
        {
            return $"分析 {assetSymbol} 未能完成: 仅 {completedAnalysts}/{totalAnalysts} 位分析师完成了分析，报告生成被中断";
        }

        return $"分析 {assetSymbol} 的所有分析师已完成，但综合报告生成环节异常，请重试。如果问题持续，请检查 AI 模型配置是否正确";
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
                var agent = _analystAgentFactory.CreateAnalyst(type, runtime, sharedProviders)
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
    private static string GetDisplayNameForExecutorId(
        string executorId,
        IReadOnlyDictionary<string, string> agentNameToDisplayName)
    {
        var namePrefix = GetExecutorNamePrefix(executorId);

        return agentNameToDisplayName.TryGetValue(namePrefix, out var displayName)
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
    private Workflow BuildWorkflow(
        List<AIAgent> analystAgents,
        AnalysisAggregatorExecutor aggregatorExecutor,
        CoordinatorExecutor coordinatorExecutor)
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

        // 1. 创建 Dispatcher（作为入口节点）
        var dispatcher = new AnalysisDispatcherExecutor(
            _loggerFactory.CreateLogger<AnalysisDispatcherExecutor>());

        // 2. 创建工作流，Dispatcher 作为入口节点
        var builder = new WorkflowBuilder(dispatcher);

        // 3. Fan-Out: Dispatcher → 所有分析师（Dispatcher 广播 ChatMessage）
        // AIAgent 可以直接用于工作流，框架会自动处理
        builder.AddFanOutEdge(dispatcher, [.. analystAgents]);

        // 4. Fan-In: 所有分析师 → Aggregator
        // 框架会自动收集所有源（分析师）的消息，并作为 List<ChatMessage> 一次性传递给 Aggregator
        builder.AddFanInBarrierEdge([.. analystAgents], aggregatorExecutor);

        // 5. Aggregator → Coordinator（将聚合结果传递给协调分析师）
        builder.AddEdge(aggregatorExecutor, coordinatorExecutor);

        // 6. 设置输出来自 Coordinator
        builder.WithOutputFrom(coordinatorExecutor);

        return builder.Build();
    }

    /// <summary>
    /// 触发进度事件
    /// </summary>
    protected virtual void OnProgressChanged(
        Guid runId,
        string assetSymbol,
        AnalysisProgressEventArgs e)
    {
        e.RunId = runId;
        e.AssetSymbol = assetSymbol;
        ProgressChanged?.Invoke(this, e);
    }
}

/// <summary>
/// 分析进度变化事件参数
/// </summary>
public sealed class AnalysisProgressEventArgs : EventArgs
{
    /// <summary>
    /// 本次分析运行的唯一标识。
    /// </summary>
    public Guid RunId { get; internal set; }

    /// <summary>
    /// 本次分析对应的标的代码。
    /// </summary>
    public string AssetSymbol { get; internal set; } = string.Empty;

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
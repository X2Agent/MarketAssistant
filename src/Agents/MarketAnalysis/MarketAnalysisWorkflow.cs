using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场分析并发工作流（基于 Agent Framework 最佳实践）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/agents-in-workflows
/// </summary>
public class MarketAnalysisWorkflow : IDisposable
{
    private readonly AnalysisAggregatorExecutor _aggregatorExecutor;
    private readonly CoordinatorExecutor _coordinatorExecutor;
    private readonly IUserSettingService _userSettingService;
    private readonly IAnalystAgentFactory _analystAgentFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MarketAnalysisWorkflow> _logger;

    private bool _disposed = false;

    /// <summary>
    /// 分析进度事件
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// 单个分析师结果接收事件
    /// </summary>
    public event EventHandler<ChatMessage>? AnalystResultReceived;

    public MarketAnalysisWorkflow(
        AnalysisAggregatorExecutor aggregatorExecutor,
        CoordinatorExecutor coordinatorExecutor,
        IUserSettingService userSettingService,
        IAnalystAgentFactory analystAgentFactory,
        ILoggerFactory loggerFactory,
        ILogger<MarketAnalysisWorkflow> logger)
    {
        _aggregatorExecutor = aggregatorExecutor ?? throw new ArgumentNullException(nameof(aggregatorExecutor));
        _coordinatorExecutor = coordinatorExecutor ?? throw new ArgumentNullException(nameof(coordinatorExecutor));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _analystAgentFactory = analystAgentFactory ?? throw new ArgumentNullException(nameof(analystAgentFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行市场分析工作流
    /// </summary>
    public async Task<MarketAnalysisReport> AnalyzeAsync(
        string stockSymbol,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始执行市场分析工作流，股票代码: {StockSymbol}", stockSymbol);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                CurrentAnalyst = "系统",
                StageDescription = "正在准备分析环境",
                IsInProgress = true
            });

            // 获取启用的分析师列表
            var enabledAnalysts = GetEnabledAnalysts();
            if (enabledAnalysts.Count == 0)
            {
                throw new InvalidOperationException("没有启用任何分析师，请在设置中至少启用一位分析师");
            }

            // 创建分析师代理
            var analystAgents = CreateAnalystAgents(enabledAnalysts);

            // 构建工作流（传入分析师数量）
            var workflow = BuildWorkflow(analystAgents.Count, analystAgents);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                CurrentAnalyst = "分析师团队",
                StageDescription = $"{enabledAnalysts.Count} 位分析师正在并发分析",
                IsInProgress = true
            });

            // 执行工作流（流式处理）
            var finalReport = await ExecuteWorkflowAsync(workflow, stockSymbol, cancellationToken);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                CurrentAnalyst = "系统",
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
                CurrentAnalyst = "系统",
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
        string stockSymbol,
        CancellationToken cancellationToken)
    {
        MarketAnalysisReport? finalReport = null;

        // 使用流式执行，将股票代码作为输入
        // Dispatcher 会接收股票代码并广播给所有分析师
        await using StreamingRun run = await InProcessExecution.StreamAsync(
            workflow,
            stockSymbol,
            runId: null,
            cancellationToken);

        // 发送 TurnToken 触发工作流开始处理
        // 根据官方文档：代理会缓存消息，只有收到 TurnToken 才开始处理
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        // 监听工作流事件
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ExecutorInvokedEvent executorInvoked:
                    _logger.LogDebug("工作流步骤开始: {ExecutorId}", executorInvoked.ExecutorId);

                    // 更新进度：显示当前正在执行的步骤
                    string stageName = executorInvoked.ExecutorId switch
                    {
                        "AnalysisDispatcher" => "正在分发分析任务",
                        "AnalysisAggregator" => "正在聚合分析结果",
                        "Coordinator" => "正在生成综合报告",
                        _ => "正在分析"
                    };

                    OnProgressChanged(new AnalysisProgressEventArgs
                    {
                        CurrentAnalyst = executorInvoked.ExecutorId,
                        StageDescription = stageName,
                        IsInProgress = true
                    });
                    break;

                case ExecutorCompletedEvent executorComplete:
                    _logger.LogDebug("工作流步骤完成: {ExecutorId}", executorComplete.ExecutorId);
                    break;

                case AgentRunUpdateEvent agentUpdate:
                    // 处理代理的流式更新
                    _logger.LogDebug("代理更新 [{AgentId}]: {Data}",
                        agentUpdate.ExecutorId,
                        agentUpdate.Data);

                    OnProgressChanged(new AnalysisProgressEventArgs
                    {
                        CurrentAnalyst = agentUpdate.ExecutorId,
                        StageDescription = $"正在分析... {agentUpdate.Data}",
                        IsInProgress = true
                    });
                    break;

                case WorkflowOutputEvent workflowOutput:
                    finalReport = workflowOutput.Data as MarketAnalysisReport;
                    _logger.LogInformation("工作流完成，生成最终报告");

                    // 触发每个分析师的消息接收事件
                    if (finalReport != null)
                    {
                        foreach (var message in finalReport.AnalystMessages)
                        {
                            OnAnalystResultReceived(message);
                        }

                        // 触发协调分析师的最终报告（使用 Summary 作为简短总结）
                        if (!string.IsNullOrEmpty(finalReport.CoordinatorResult.Summary))
                        {
                            var coordinatorMessage = new ChatMessage(
                                ChatRole.Assistant,
                                finalReport.CoordinatorResult.Summary)
                            {
                                AuthorName = "CoordinatorAnalystAgent"
                            };
                            OnAnalystResultReceived(coordinatorMessage);
                        }
                    }
                    break;
            }
        }

        return finalReport ?? throw new InvalidOperationException("工作流未返回分析报告");
    }

    /// <summary>
    /// 获取启用的分析师列表
    /// </summary>
    private List<AnalystType> GetEnabledAnalysts()
    {
        var roleSettings = _userSettingService.CurrentSetting.AnalystRoleSettings;
        var enabledAnalysts = new List<AnalystType>();

        if (roleSettings.EnableFundamentalAnalyst)
            enabledAnalysts.Add(AnalystType.FundamentalAnalyst);

        if (roleSettings.EnableMarketSentimentAnalyst)
            enabledAnalysts.Add(AnalystType.MarketSentimentAnalyst);

        if (roleSettings.EnableFinancialAnalyst)
            enabledAnalysts.Add(AnalystType.FinancialAnalyst);

        if (roleSettings.EnableTechnicalAnalyst)
            enabledAnalysts.Add(AnalystType.TechnicalAnalyst);

        if (roleSettings.EnableNewsEventAnalyst)
            enabledAnalysts.Add(AnalystType.NewsEventAnalyst);

        return enabledAnalysts;
    }

    /// <summary>
    /// 创建分析师代理（使用 Factory 模式）
    /// </summary>
    private List<AIAgent> CreateAnalystAgents(List<AnalystType> analystTypes)
    {
        _logger.LogInformation("开始创建分析师代理，数量: {Count}", analystTypes.Count);
        var createdAgents = _analystAgentFactory.CreateAnalysts(analystTypes);
        _logger.LogInformation("成功创建分析师代理，实际数量: {Count}", createdAgents.Count);
        return createdAgents;
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
        // [Dispatcher] string (stockSymbol) → broadcast ChatMessage
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
        builder.AddFanInEdge([.. analystAgents], _aggregatorExecutor);

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

    /// <summary>
    /// 触发分析师结果接收事件
    /// </summary>
    protected virtual void OnAnalystResultReceived(ChatMessage message)
    {
        AnalystResultReceived?.Invoke(this, message);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 分析进度变化事件参数
/// </summary>
public sealed class AnalysisProgressEventArgs : EventArgs
{
    /// <summary>
    /// 当前工作的分析师名称
    /// </summary>
    public string CurrentAnalyst { get; set; } = string.Empty;

    /// <summary>
    /// 当前阶段描述
    /// </summary>
    public string StageDescription { get; set; } = string.Empty;

    /// <summary>
    /// 是否正在进行中
    /// </summary>
    public bool IsInProgress { get; set; } = true;
}
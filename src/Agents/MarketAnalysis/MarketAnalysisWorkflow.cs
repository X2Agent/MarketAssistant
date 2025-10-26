using MarketAssistant.Agents.MarketAnalysis.Executors;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场分析并发工作流（基于 Agent Framework 最佳实践）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/agents-in-workflows
/// </summary>
public class MarketAnalysisWorkflow : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IUserSettingService _userSettingService;
    private readonly IKernelPluginConfig _kernelPluginConfig;
    private readonly Kernel _kernel;
    private readonly ILogger<MarketAnalysisWorkflow> _logger;
    private bool _disposed = false;

    /// <summary>
    /// 分析进度事件
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs>? ProgressChanged;

    public MarketAnalysisWorkflow(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        IUserSettingService userSettingService,
        IKernelPluginConfig kernelPluginConfig,
        Kernel kernel,
        ILogger<MarketAnalysisWorkflow> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _kernelPluginConfig = kernelPluginConfig ?? throw new ArgumentNullException(nameof(kernelPluginConfig));
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行市场分析工作流
    /// </summary>
    public async Task<MarketAnalysisReport> AnalyzeAsync(
        string stockSymbol,
        string? customPrompt = null,
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

            // 创建分析请求
            var request = new MarketAnalysisRequest
            {
                StockSymbol = stockSymbol,
                Prompt = customPrompt
            };

            // 获取启用的分析师列表
            var enabledAnalysts = GetEnabledAnalysts();
            if (enabledAnalysts.Count == 0)
            {
                throw new InvalidOperationException("没有启用任何分析师，请在设置中至少启用一位分析师");
            }

            // 创建分析师代理
            var analystAgents = CreateAnalystAgents(enabledAnalysts);

            // 构建工作流
            var workflow = BuildWorkflow(request, analystAgents);

            OnProgressChanged(new AnalysisProgressEventArgs
            {
                CurrentAnalyst = "分析师团队",
                StageDescription = $"{enabledAnalysts.Count} 位分析师正在并发分析",
                IsInProgress = true
            });

            // 执行工作流（流式处理）
            var finalReport = await ExecuteWorkflowAsync(workflow, request, cancellationToken);

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
        MarketAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        MarketAnalysisReport? finalReport = null;

        // 使用流式执行，将 MarketAnalysisRequest 作为输入
        // Dispatcher 会接收此请求并广播给所有分析师
        await using StreamingRun run = await InProcessExecution.StreamAsync(
            workflow,
            request,
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
                    break;
            }
        }

        return finalReport ?? throw new InvalidOperationException("工作流未返回分析报告");
    }

    /// <summary>
    /// 获取启用的分析师列表
    /// </summary>
    private List<AnalysisAgents> GetEnabledAnalysts()
    {
        var roleSettings = _userSettingService.CurrentSetting.AnalystRoleSettings;
        var enabledAnalysts = new List<AnalysisAgents>();

        if (roleSettings.EnableFundamentalAnalyst)
            enabledAnalysts.Add(AnalysisAgents.FundamentalAnalystAgent);

        if (roleSettings.EnableMarketSentimentAnalyst)
            enabledAnalysts.Add(AnalysisAgents.MarketSentimentAnalystAgent);

        if (roleSettings.EnableFinancialAnalyst)
            enabledAnalysts.Add(AnalysisAgents.FinancialAnalystAgent);

        if (roleSettings.EnableTechnicalAnalyst)
            enabledAnalysts.Add(AnalysisAgents.TechnicalAnalystAgent);

        if (roleSettings.EnableNewsEventAnalyst)
            enabledAnalysts.Add(AnalysisAgents.NewsEventAnalystAgent);

        return enabledAnalysts;
    }

    /// <summary>
    /// 创建分析师代理
    /// </summary>
    private List<ChatClientAgent> CreateAnalystAgents(List<AnalysisAgents> analystTypes)
    {
        var agents = new List<ChatClientAgent>();
        var chatClient = _chatClientFactory.CreateClient();

        foreach (var analystType in analystTypes)
        {
            try
            {
                // 加载 YAML 配置
                string yamlPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Agents",
                    "Yaml",
                    $"{analystType}.yaml");

                if (!File.Exists(yamlPath))
                {
                    _logger.LogWarning("未找到分析师配置文件: {YamlPath}，跳过该分析师", yamlPath);
                    continue;
                }

                string yamlContent = File.ReadAllText(yamlPath);
                PromptTemplateConfig templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

                // 创建 ChatClientAgent（直接作为工作流节点）
                var agent = new ChatClientAgent(
                    chatClient,
                    name: analystType.ToString(),
                    instructions: templateConfig.Template ?? "你是一位专业的市场分析师。");

                agents.Add(agent);

                _logger.LogInformation("成功创建分析师代理: {AnalystName}", analystType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分析师代理时发生错误: {AnalystType}", analystType);
            }
        }

        return agents;
    }

    /// <summary>
    /// 构建并发工作流（Fan-Out/Fan-In 模式）
    /// 分发器 → 多个分析师并发工作 → 聚合器 → 协调分析师
    /// </summary>
    private Workflow BuildWorkflow(
        MarketAnalysisRequest request,
        List<ChatClientAgent> analystAgents)
    {
        // 创建 Executors
        var dispatcher = new AnalysisDispatcherExecutor(
            _serviceProvider.GetRequiredService<ILogger<AnalysisDispatcherExecutor>>());

        var aggregator = new AnalysisAggregatorExecutor(
            request,
            analystAgents.Count,
            _serviceProvider.GetRequiredService<ILogger<AnalysisAggregatorExecutor>>());

        var coordinator = new CoordinatorExecutor(
            _chatClientFactory.CreateClient(),
            _serviceProvider.GetRequiredService<ILogger<CoordinatorExecutor>>());

        // 构建工作流：
        // [Dispatcher] 接收请求并广播
        //      ↓ ↓ ↓ ↓ (Fan-Out: 广播给所有分析师)
        // [Analyst1] [Analyst2] [Analyst3] ... (并发执行)
        //      ↓ ↓ ↓ ↓ (Fan-In: 收集所有结果)
        // [Aggregator] 聚合所有分析结果
        //      ↓
        // [Coordinator] 生成最终综合报告
        //      ↓
        // MarketAnalysisReport (输出)

        // 1. 创建工作流，Dispatcher 作为入口节点
        var builder = new WorkflowBuilder(dispatcher);

        // 2. Fan-Out: Dispatcher 连接到所有分析师（并发广播）
        foreach (var analyst in analystAgents)
        {
            builder.AddEdge(dispatcher, analyst);
        }

        // 3. Fan-In: 所有分析师的输出聚合到 Aggregator
        builder.AddFanInEdge(aggregator, sources: [.. analystAgents]);

        // 4. Aggregator 连接到 Coordinator
        builder.AddEdge(aggregator, coordinator);

        // 5. 设置输出来自 Coordinator
        builder.WithOutputFrom(coordinator);

        return builder.Build();
    }

    /// <summary>
    /// 触发进度事件
    /// </summary>
    protected virtual void OnProgressChanged(AnalysisProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
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

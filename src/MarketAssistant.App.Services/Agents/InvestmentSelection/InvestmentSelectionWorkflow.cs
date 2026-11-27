using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection;

/// <summary>
/// AI 投资选择工作流，使用 Agent Framework Workflows 实现确定性三步骤流程：
/// 生成筛选条件 → 执行筛选 → AI 分析结果。
/// </summary>
/// <remarks>
/// 与 MarketAnalysisWorkflow 对齐：4 个 Executor 均为 Transient 注册，
/// 由工作流在每次 Run 内从容器重新解析，
/// 避免 Singleton Executor 在并发分析间共享可变状态与模型/运行时引用。
/// </remarks>
public class InvestmentSelectionWorkflow(
    IServiceProvider serviceProvider,
    ILogger<InvestmentSelectionWorkflow> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<InvestmentSelectionWorkflow> _logger = logger;

    /// <summary>
    /// 执行基于用户需求的 AI 投资分析；<paramref name="progress"/> 接收三步骤的开始/完成事件。
    /// </summary>
    public async Task<InvestmentSelectionResult> AnalyzeUserRequirementAsync(
        InvestmentRecommendationRequest request,
        IProgress<InvestmentSelectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workflowRequest = new InvestmentSelectionWorkflowRequest
        {
            MarketType = request.MarketType,
            IsNewsAnalysis = false,
            Content = request.UserRequirements,
            RiskPreference = request.RiskPreference,
            InvestmentAmount = request.InvestmentAmount,
            InvestmentHorizon = request.InvestmentHorizon,
            PreferredSectors = request.PreferredSectors,
            ExcludedSectors = request.ExcludedSectors,
            MaxRecommendations = request.MaxRecommendations
        };

        return await ExecuteWorkflowAsync(workflowRequest, progress, cancellationToken);
    }

    /// <summary>
    /// 执行基于新闻内容的 AI 投资分析；<paramref name="progress"/> 接收三步骤的开始/完成事件。
    /// </summary>
    public async Task<InvestmentSelectionResult> AnalyzeNewsHotspotAsync(
        NewsBasedInvestmentRequest request,
        IProgress<InvestmentSelectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workflowRequest = new InvestmentSelectionWorkflowRequest
        {
            MarketType = request.MarketType,
            IsNewsAnalysis = true,
            Content = request.NewsContent,
            MaxRecommendations = request.MaxRecommendations
        };

        return await ExecuteWorkflowAsync(workflowRequest, progress, cancellationToken);
    }

    private async Task<InvestmentSelectionResult> ExecuteWorkflowAsync(
        InvestmentSelectionWorkflowRequest request,
        IProgress<InvestmentSelectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "开始执行投资选择工作流，市场类型: {MarketType}，分析类型: {Type}",
            request.MarketType,
            request.IsNewsAnalysis ? "新闻热点" : "用户需求");

        // 每次运行新建 Executor 实例（Transient），避免并发 Run 间共享状态
        var generateStockCriteriaExecutor = _serviceProvider.GetRequiredService<GenerateCriteriaExecutor<StockCriteria>>();
        var generateCryptoCriteriaExecutor = _serviceProvider.GetRequiredService<GenerateCriteriaExecutor<CryptoCriteria>>();
        var screenTargetsExecutor = _serviceProvider.GetRequiredService<ScreenInvestmentTargetsExecutor>();
        var analyzeAssetsExecutor = _serviceProvider.GetRequiredService<AnalyzeAssetsExecutor>();

        WorkflowBuilder workflowBuilder = request.MarketType switch
        {
            MarketType.AShare => new WorkflowBuilder(generateStockCriteriaExecutor)
                .AddEdge(generateStockCriteriaExecutor, screenTargetsExecutor)
                .AddEdge(screenTargetsExecutor, analyzeAssetsExecutor)
                .WithOutputFrom(analyzeAssetsExecutor),

            MarketType.Crypto => new WorkflowBuilder(generateCryptoCriteriaExecutor)
                .AddEdge(generateCryptoCriteriaExecutor, screenTargetsExecutor)
                .AddEdge(screenTargetsExecutor, analyzeAssetsExecutor)
                .WithOutputFrom(analyzeAssetsExecutor),

            _ => throw new NotSupportedException($"不支持的市场类型: {request.MarketType}")
        };

        var workflow = workflowBuilder.Build();
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow,
            request,
            cancellationToken: cancellationToken);

        InvestmentSelectionResult? finalResult = null;

        await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ExecutorInvokedEvent executorInvoked:
                    _logger.LogInformation("步骤开始: {ExecutorId}", executorInvoked.ExecutorId);
                    if (ToStep(executorInvoked.ExecutorId) is { } startedStep)
                    {
                        progress?.Report(new InvestmentSelectionProgress(
                            startedStep, InvestmentSelectionStepStatus.Running));
                    }
                    break;
                case ExecutorCompletedEvent executorCompleted:
                    _logger.LogInformation("步骤完成: {ExecutorId}", executorCompleted.ExecutorId);
                    if (ToStep(executorCompleted.ExecutorId) is { } completedStep)
                    {
                        progress?.Report(new InvestmentSelectionProgress(
                            completedStep,
                            InvestmentSelectionStepStatus.Completed,
                            DescribeStepResult(executorCompleted.Data)));
                    }
                    break;
                // AgentResponseUpdateEvent 继承自 WorkflowOutputEvent，必须在其之前匹配，
                // 否则 LLM 的中间流式更新会被误当作最终输出。
                case AgentResponseUpdateEvent:
                    break;
                case WorkflowOutputEvent workflowOutput:
                    finalResult = workflowOutput.Data as InvestmentSelectionResult;
                    _logger.LogInformation(
                        "工作流完成，推荐数量: {Count}",
                        finalResult?.Recommendations?.Count ?? 0);
                    break;
                case ExecutorFailedEvent executorFailed:
                    var failedMessage = executorFailed.Data?.Message ?? "未知错误";
                    _logger.LogError(
                        executorFailed.Data,
                        "步骤失败: {ExecutorId}, 错误: {Error}",
                        executorFailed.ExecutorId,
                        failedMessage);
                    throw new FriendlyException(failedMessage);
                case WorkflowErrorEvent workflowError:
                    var workflowErrorMessage = workflowError.Exception?.Message ?? "工作流内部发生未知错误";
                    _logger.LogError(
                        workflowError.Exception,
                        "投资选择工作流发生严重错误: {Message}",
                        workflowErrorMessage);
                    throw new FriendlyException(workflowErrorMessage);
                case WorkflowWarningEvent workflowWarning:
                    _logger.LogWarning("投资选择工作流警告: {Warning}", workflowWarning.Data);
                    break;
            }
        }

        // 事件流未产出结果说明工作流中途失败（各 Executor 已把异常包装上抛），
        // 把"失败"伪装成结构完整的默认结果会让用户误以为"AI 认为没有合适标的"
        if (finalResult == null)
            throw new FriendlyException("投资选择工作流未返回结果，分析未完成，请重试。");

        return finalResult;
    }

    private static InvestmentSelectionStep? ToStep(string executorId)
    {
        if (executorId.StartsWith("GenerateCriteria", StringComparison.Ordinal))
            return InvestmentSelectionStep.GenerateCriteria;
        if (executorId.StartsWith("ScreenInvestmentTargets", StringComparison.Ordinal))
            return InvestmentSelectionStep.ScreenTargets;
        if (executorId.StartsWith("AnalyzeAssets", StringComparison.Ordinal))
            return InvestmentSelectionStep.AnalyzeAssets;
        return null;
    }

    private static string? DescribeStepResult(object? data) => data switch
    {
        CriteriaGenerationResult { CriteriaCount: > 0 } criteria => $"已生成 {criteria.CriteriaCount} 个筛选条件",
        AssetScreeningResult screening => $"候选 {screening.ScreenedAssets.Count} 个标的",
        _ => null
    };
}

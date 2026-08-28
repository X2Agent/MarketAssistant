using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection;

/// <summary>
/// AI 投资选择工作流，使用 Agent Framework Workflows 实现确定性三步骤流程：
/// 生成筛选条件 → 执行筛选 → AI 分析结果。
/// </summary>
public class InvestmentSelectionWorkflow : IDisposable
{
    private readonly GenerateCriteriaExecutor<StockCriteria> _generateStockCriteriaExecutor;
    private readonly GenerateCriteriaExecutor<CryptoCriteria> _generateCryptoCriteriaExecutor;
    private readonly ScreenInvestmentTargetsExecutor _screenTargetsExecutor;
    private readonly AnalyzeAssetsExecutor _analyzeAssetsExecutor;
    private readonly ILogger<InvestmentSelectionWorkflow> _logger;
    private bool _disposed;

    public InvestmentSelectionWorkflow(
        GenerateCriteriaExecutor<StockCriteria> generateStockCriteriaExecutor,
        GenerateCriteriaExecutor<CryptoCriteria> generateCryptoCriteriaExecutor,
        ScreenInvestmentTargetsExecutor screenTargetsExecutor,
        AnalyzeAssetsExecutor analyzeAssetsExecutor,
        ILogger<InvestmentSelectionWorkflow> logger)
    {
        _generateStockCriteriaExecutor = generateStockCriteriaExecutor ?? throw new ArgumentNullException(nameof(generateStockCriteriaExecutor));
        _generateCryptoCriteriaExecutor = generateCryptoCriteriaExecutor ?? throw new ArgumentNullException(nameof(generateCryptoCriteriaExecutor));
        _screenTargetsExecutor = screenTargetsExecutor ?? throw new ArgumentNullException(nameof(screenTargetsExecutor));
        _analyzeAssetsExecutor = analyzeAssetsExecutor ?? throw new ArgumentNullException(nameof(analyzeAssetsExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行基于用户需求的 AI 投资分析。
    /// </summary>
    public async Task<InvestmentSelectionResult> AnalyzeUserRequirementAsync(
        InvestmentRecommendationRequest request,
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

        return await ExecuteWorkflowAsync(workflowRequest, cancellationToken);
    }

    /// <summary>
    /// 执行基于新闻内容的 AI 投资分析。
    /// </summary>
    public async Task<InvestmentSelectionResult> AnalyzeNewsHotspotAsync(
        NewsBasedInvestmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var workflowRequest = new InvestmentSelectionWorkflowRequest
        {
            MarketType = request.MarketType,
            IsNewsAnalysis = true,
            Content = request.NewsContent,
            MaxRecommendations = request.MaxRecommendations
        };

        return await ExecuteWorkflowAsync(workflowRequest, cancellationToken);
    }

    private async Task<InvestmentSelectionResult> ExecuteWorkflowAsync(
        InvestmentSelectionWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "开始执行投资选择工作流，市场类型: {MarketType}，分析类型: {Type}",
            request.MarketType,
            request.IsNewsAnalysis ? "新闻热点" : "用户需求");

        WorkflowBuilder workflowBuilder = request.MarketType switch
        {
            MarketType.AShare => new WorkflowBuilder(_generateStockCriteriaExecutor)
                .AddEdge(_generateStockCriteriaExecutor, _screenTargetsExecutor)
                .AddEdge(_screenTargetsExecutor, _analyzeAssetsExecutor)
                .WithOutputFrom(_analyzeAssetsExecutor),

            MarketType.Crypto => new WorkflowBuilder(_generateCryptoCriteriaExecutor)
                .AddEdge(_generateCryptoCriteriaExecutor, _screenTargetsExecutor)
                .AddEdge(_screenTargetsExecutor, _analyzeAssetsExecutor)
                .WithOutputFrom(_analyzeAssetsExecutor),

            _ => throw new NotSupportedException($"不支持的市场类型: {request.MarketType}")
        };

        var workflow = workflowBuilder.Build();
        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            request,
            cancellationToken: cancellationToken);

        InvestmentSelectionResult? finalResult = null;

        foreach (WorkflowEvent evt in run.NewEvents)
        {
            switch (evt)
            {
                case ExecutorInvokedEvent executorInvoked:
                    _logger.LogInformation("步骤开始: {ExecutorId}", executorInvoked.ExecutorId);
                    break;
                case ExecutorCompletedEvent executorCompleted:
                    _logger.LogInformation("步骤完成: {ExecutorId}", executorCompleted.ExecutorId);
                    break;
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

    private static InvestmentSelectionResult CreateDefaultResult(string? problem = null)
    {
        return new InvestmentSelectionResult
        {
            Recommendations = [],
            ConfidenceScore = 0,
            AnalysisSummary = problem ?? "分析过程中遇到问题，请稍后重试。",
            MarketEnvironmentAnalysis = "无可用分析",
            InvestmentAdvice = "建议稍后重试",
            RiskWarnings = ["系统异常，请联系技术支持"]
        };
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

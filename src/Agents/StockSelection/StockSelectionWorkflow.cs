using MarketAssistant.Agents.StockSelection.Executors;
using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.StockSelection;

/// <summary>
/// AI选股工作流，使用 Agent Framework Workflows 实现确定性三步骤流程
/// 第1步: 生成筛选条件 → 第2步: 执行筛选 → 第3步: AI分析结果
/// </summary>
public class StockSelectionWorkflow : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<StockSelectionWorkflow> _logger;
    private bool _disposed = false;

    public StockSelectionWorkflow(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        ILogger<StockSelectionWorkflow> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行基于用户需求的AI选股分析（使用工作流）
    /// </summary>
    public async Task<StockSelectionResult> AnalyzeUserRequirementAsync(
        StockRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workflowRequest = new StockSelectionWorkflowRequest
        {
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
    /// 执行基于新闻内容的AI选股分析（使用工作流）
    /// </summary>
    public async Task<StockSelectionResult> AnalyzeNewsHotspotAsync(
        NewsBasedSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var workflowRequest = new StockSelectionWorkflowRequest
        {
            IsNewsAnalysis = true,
            Content = request.NewsContent,
            MaxRecommendations = request.MaxRecommendations
        };

        return await ExecuteWorkflowAsync(workflowRequest, cancellationToken);
    }

    /// <summary>
    /// 执行完整的选股工作流（确定性三步骤）
    /// </summary>
    private async Task<StockSelectionResult> ExecuteWorkflowAsync(
        StockSelectionWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("开始执行选股工作流，分析类型: {Type}",
                request.IsNewsAnalysis ? "新闻热点" : "用户需求");

            // 创建三个 Executor
            var generateCriteriaExecutor = new GenerateCriteriaExecutor(
                _chatClientFactory,
                _serviceProvider.GetRequiredService<ILogger<GenerateCriteriaExecutor>>()
            );

            var screenStocksExecutor = new ScreenStocksExecutor(
                _serviceProvider,
                _serviceProvider.GetRequiredService<ILogger<ScreenStocksExecutor>>()
            );

            var chatClient = _chatClientFactory.CreateClient();

            var analyzeStocksExecutor = new AnalyzeStocksExecutor(
                chatClient,
                request,
                _serviceProvider.GetRequiredService<ILogger<AnalyzeStocksExecutor>>()
            );

            // 构建顺序工作流: 步骤1 → 步骤2 → 步骤3
            var builder = new WorkflowBuilder(generateCriteriaExecutor);
            builder
                .AddEdge(generateCriteriaExecutor, screenStocksExecutor)
                .AddEdge(screenStocksExecutor, analyzeStocksExecutor)
                .WithOutputFrom(analyzeStocksExecutor);

            var workflow = builder.Build();

            // 执行工作流
            await using Run run = await InProcessExecution.RunAsync(workflow, request, runId: null, cancellationToken);

            StockSelectionResult? finalResult = null;

            // 处理工作流事件
            foreach (WorkflowEvent evt in run.NewEvents)
            {
                switch (evt)
                {
                    case ExecutorInvokedEvent executorInvoked:
                        _logger.LogInformation("步骤开始: {ExecutorId}", executorInvoked.ExecutorId);
                        break;

                    case ExecutorCompletedEvent executorComplete:
                        _logger.LogInformation("步骤完成: {ExecutorId}", executorComplete.ExecutorId);
                        break;

                    case WorkflowOutputEvent workflowOutput:
                        finalResult = workflowOutput.Data as StockSelectionResult;
                        _logger.LogInformation("工作流完成，推荐股票数量: {Count}",
                            finalResult?.Recommendations?.Count ?? 0);
                        break;
                }
            }

            return finalResult ?? CreateDefaultResult("工作流未返回结果");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工作流执行失败");
            return CreateDefaultResult($"工作流执行异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 创建默认结果
    /// </summary>
    private StockSelectionResult CreateDefaultResult(string? problem = null)
    {
        return new StockSelectionResult
        {
            Recommendations = new List<StockRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = problem ?? "分析过程中遇到问题，请稍后重试。"
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


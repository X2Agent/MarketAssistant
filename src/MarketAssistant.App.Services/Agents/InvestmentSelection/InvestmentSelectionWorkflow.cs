using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection;

/// <summary>
/// AI投资选择工作流，确定性三步骤流程：
/// 第1步: 生成筛选条件 → 第2步: 执行筛选 → 第3步: AI分析结果
/// 根据市场类型（股票/虚拟币）选择对应的条件生成 Executor
/// </summary>
public class InvestmentSelectionWorkflow
{
    private readonly GenerateCriteriaExecutor<StockCriteria> _generateStockCriteriaExecutor;
    private readonly GenerateCriteriaExecutor<CryptoCriteria> _generateCryptoCriteriaExecutor;
    private readonly ScreenInvestmentTargetsExecutor _screenTargetsExecutor;
    private readonly AnalyzeAssetsExecutor _analyzeAssetsExecutor;
    private readonly ILogger<InvestmentSelectionWorkflow> _logger;

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
    /// 执行基于用户需求的AI投资分析
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
    /// 执行基于新闻内容的AI投资分析
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

    /// <summary>
    /// 执行完整的投资选择工作流（确定性三步骤）
    /// </summary>
    private async Task<InvestmentSelectionResult> ExecuteWorkflowAsync(
        InvestmentSelectionWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始执行投资选择工作流，市场类型: {MarketType}，分析类型: {Type}",
            request.MarketType,
            request.IsNewsAnalysis ? "新闻热点" : "用户需求");

        // 步骤1: 根据市场类型选择对应的条件生成器
        CriteriaGenerationResult criteriaResult = request.MarketType switch
        {
            MarketType.AShare => await _generateStockCriteriaExecutor.HandleAsync(request, cancellationToken),
            MarketType.Crypto => await _generateCryptoCriteriaExecutor.HandleAsync(request, cancellationToken),
            _ => throw new NotSupportedException($"不支持的市场类型: {request.MarketType}")
        };

        // 步骤2: 执行筛选
        var screeningResult = await _screenTargetsExecutor.HandleAsync(criteriaResult, cancellationToken);

        // 步骤3: AI 分析
        var finalResult = await _analyzeAssetsExecutor.HandleAsync(screeningResult, cancellationToken);

        _logger.LogInformation("工作流完成，推荐数量: {Count}",
            finalResult?.Recommendations?.Count ?? 0);

        return finalResult ?? CreateDefaultResult("工作流未返回结果");
    }

    private InvestmentSelectionResult CreateDefaultResult(string? problem = null)
    {
        return new InvestmentSelectionResult
        {
            Recommendations = new List<InvestmentRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = problem ?? "分析过程中遇到问题，请稍后重试。",
            MarketEnvironmentAnalysis = "无可用分析",
            InvestmentAdvice = "建议稍后重试",
            RiskWarnings = new List<string> { "系统异常，请联系技术支持" }
        };
    }
}

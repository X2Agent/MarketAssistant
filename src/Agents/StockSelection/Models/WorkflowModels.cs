using MarketAssistant.Services.StockScreener.Models;

namespace MarketAssistant.Agents.StockSelection.Models;

/// <summary>
/// 工作流请求模型（统一的输入）
/// </summary>
public record StockSelectionWorkflowRequest
{
    /// <summary>
    /// 是否为新闻分析（true=新闻分析，false=用户需求分析）
    /// </summary>
    public bool IsNewsAnalysis { get; init; }

    // 用户需求分析相关字段
    public string? Content { get; init; }
    public string? RiskPreference { get; init; }
    public decimal? InvestmentAmount { get; init; }
    public int? InvestmentHorizon { get; init; }
    public List<string> PreferredSectors { get; init; } = new();
    public List<string> ExcludedSectors { get; init; } = new();
    public int MaxRecommendations { get; init; } = 10;
}

/// <summary>
/// 步骤1的输出：筛选条件生成结果
/// </summary>
public record CriteriaGenerationResult
{
    /// <summary>
    /// 生成的筛选条件
    /// </summary>
    public StockCriteria Criteria { get; init; } = new();

    /// <summary>
    /// 原始请求（用于传递到后续步骤）
    /// </summary>
    public StockSelectionWorkflowRequest OriginalRequest { get; init; } = new();
}

/// <summary>
/// 步骤2的输出：筛选结果
/// </summary>
public record ScreeningResult
{
    /// <summary>
    /// 筛选得到的股票列表
    /// </summary>
    public List<ScreenerStockInfo> ScreenedStocks { get; init; } = new();

    /// <summary>
    /// 使用的筛选条件
    /// </summary>
    public StockCriteria? Criteria { get; init; }

    /// <summary>
    /// 原始请求信息（用于步骤3分析）
    /// </summary>
    public StockSelectionWorkflowRequest? OriginalRequest { get; init; }
}


using MarketAssistant.Agents.Plugins.Models;

namespace MarketAssistant.Agents.Workflows;

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
    public string? UserRequirements { get; init; }
    public string? RiskPreference { get; init; }
    public decimal? InvestmentAmount { get; init; }
    public int? InvestmentHorizon { get; init; }
    public List<string> PreferredSectors { get; init; } = new();
    public List<string> ExcludedSectors { get; init; } = new();

    // 新闻分析相关字段
    public string? NewsContent { get; init; }

    // 通用字段
    public int MaxRecommendations { get; init; } = 10;
}

/// <summary>
/// 筛选结果中间数据（步骤2的输出）
/// </summary>
public record ScreeningResult
{
    public string CriteriaJson { get; init; } = "";
    public List<ScreenerStockInfo> ScreenedStocks { get; init; } = new();
    public StockCriteria? Criteria { get; init; }
}


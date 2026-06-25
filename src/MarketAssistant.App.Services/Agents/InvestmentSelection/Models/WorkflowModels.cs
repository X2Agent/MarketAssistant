using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Applications.AssetScreener.Models;

namespace MarketAssistant.Agents.InvestmentSelection.Models;

/// <summary>
/// 工作流请求模型（统一的输入）
/// </summary>
public record InvestmentSelectionWorkflowRequest
{
    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType { get; init; } = MarketType.AShare;

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
/// 步骤1的输出：筛选条件生成结果（泛型支持不同市场）
/// </summary>
public record CriteriaGenerationResult
{
    /// <summary>
    /// 生成的筛选条件（可以是 StockCriteria 或 CryptoCriteria）
    /// </summary>
    public object Criteria { get; init; } = new();

    /// <summary>
    /// 原始请求（用于传递到后续步骤）
    /// </summary>
    public InvestmentSelectionWorkflowRequest OriginalRequest { get; init; } = new();
}

/// <summary>
/// 步骤2的输出：筛选结果
/// </summary>
public record AssetScreeningResult
{
    /// <summary>
    /// 筛选得到的资产列表
    /// </summary>
    public List<ScreenerAssetInfo> ScreenedAssets { get; init; } = new();

    /// <summary>
    /// 使用的筛选条件
    /// </summary>
    public object? Criteria { get; init; }

    /// <summary>
    /// 原始请求信息（用于步骤3分析）
    /// </summary>
    public InvestmentSelectionWorkflowRequest? OriginalRequest { get; init; }
}


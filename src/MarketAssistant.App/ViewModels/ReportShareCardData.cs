namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析报告分享卡数据（离屏渲染为图片/PDF 的输入）。
/// 仅承载展示必需字段，不暴露报告全量结构。
/// </summary>
public sealed class ReportShareCardData
{
    public string AssetName { get; init; } = string.Empty;

    public string AssetSymbol { get; init; } = string.Empty;

    /// <summary>综合评分（1-10）。</summary>
    public float OverallScore { get; init; }

    /// <summary>综合评级文本（如"买入"）。</summary>
    public string InvestmentRating { get; init; } = string.Empty;

    /// <summary>目标价文本。</summary>
    public string TargetPrice { get; init; } = string.Empty;

    /// <summary>涨跌方向文本（如"预计上涨 8-12%"）。</summary>
    public string PriceChangeExpectation { get; init; } = string.Empty;

    /// <summary>置信度百分比。</summary>
    public float ConfidencePercentage { get; init; }

    /// <summary>核心投资亮点（最多 3 条）。</summary>
    public IReadOnlyList<string> Highlights { get; init; } = [];

    /// <summary>Token 用量摘要（如"本次共调用 9 次模型 / 128.4k tokens"）。</summary>
    public string UsageSummary { get; init; } = string.Empty;

    /// <summary>生成时间（本地时间）。</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    /// <summary>涨跌方向：true 为看多（用于着色）。</summary>
    public bool IsBullish =>
        string.Equals(InvestmentRating, "强烈买入", StringComparison.Ordinal) ||
        string.Equals(InvestmentRating, "买入", StringComparison.Ordinal);

    public string ScoreText => $"{OverallScore:F1}";

    public string ConfidenceText => $"{ConfidencePercentage:F0}%";

    public string GeneratedAtText => GeneratedAt.ToString("yyyy-MM-dd HH:mm");
}

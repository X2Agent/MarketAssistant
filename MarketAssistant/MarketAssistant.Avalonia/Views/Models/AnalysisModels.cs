namespace MarketAssistant.Avalonia.Views.Models;

/// <summary>
/// 分析师结果数据模型
/// </summary>
public class AnalystResult
{
    public string ConsensusInfo { get; set; } = string.Empty;
    public string DisagreementInfo { get; set; } = string.Empty;
    public Dictionary<string, float> DimensionScores { get; set; } = new();
    public float OverallScore { get; set; }
    public float ConfidencePercentage { get; set; }
    public string Rating { get; set; } = string.Empty;
    public string TargetPrice { get; set; } = string.Empty;
    public string StockSymbol { get; set; } = string.Empty;
    public string InvestmentRating { get; set; } = string.Empty;
    public virtual string PriceChange { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public List<string> InvestmentHighlights { get; set; } = new List<string>();
    public List<string> RiskFactors { get; set; } = new List<string>();
    public List<string> OperationSuggestions { get; set; } = new List<string>();
    public List<AnalysisDataItem> AnalysisData { get; set; } = new List<AnalysisDataItem>();
}

/// <summary>
/// 通用分析数据项
/// </summary>
public class AnalysisDataItem
{
    public string DataType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
}


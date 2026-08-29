using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

[Description("分析质量自评估指标，反映本次分析的数据充分性和结论可靠度")]
public sealed class AnalysisQualityMetrics
{
    [Range(0, 100)]
    [Description("数据完整度百分比，衡量工具调用成功获取数据的维度占全部分析维度的比例。100 表示所有所需数据均已获取，0 表示完全无数据")]
    public int DataCompletenessPercent { get; set; }

    [Description("本次分析中未能获取到数据或数据不完整的维度列表，例如：'技术指标BOLL数据缺失'、'近3年财报数据不全'")]
    public List<string> MissingDataDimensions { get; set; } = [];

    [Range(0, 100)]
    [Description("分析师一致性百分比，衡量各分析师结论方向的一致程度。100 表示完全一致，0 表示完全矛盾")]
    public int AnalystConsensusPercent { get; set; }

    [MinLength(10)]
    [MaxLength(200)]
    [Description("分析的局限性说明，诚实说明可能影响结论可靠性的因素，例如数据延迟、市场剧烈波动期间分析偏差等")]
    public string LimitationsNote { get; set; } = string.Empty;

    [Description("基于数据完整度、分析师一致性和置信度的综合质量等级")]
    public AnalysisQualityLevel OverallQualityLevel { get; set; }
}

[Description("分析质量等级枚举")]
public enum AnalysisQualityLevel
{
    [Description("高质量：数据充分、分析师一致性高、置信度强")]
    High,

    [Description("中等质量：部分数据缺失或分析师存在一定分歧")]
    Medium,

    [Description("低质量：大量数据缺失或分析师严重分歧，结论仅供参考")]
    Low
}

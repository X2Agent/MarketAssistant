namespace MarketAssistant.Avalonia.Views.Models;

/// <summary>
/// 评分项目数据模型
/// </summary>
public class ScoreItem
{
    /// <summary>
    /// 评分项目名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 评分值
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// 格式化的评分显示
    /// </summary>
    public string FormattedScore => $"{Score:F1}分";

    /// <summary>
    /// 评分百分比（用于进度条显示，0-1之间）
    /// </summary>
    public double ScorePercentage => Score / 10.0;
}


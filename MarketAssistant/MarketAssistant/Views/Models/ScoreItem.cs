namespace MarketAssistant.Views.Models;

/// <summary>
/// 评分项目数据模型
/// </summary>
/// <remarks>
/// 用于UI展示各维度评分详情
/// </remarks>
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
    public double ScorePercentage => Score / 10.0; // 转换为0-1之间的值
}
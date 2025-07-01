namespace MarketAssistant.Agents;

/// <summary>
/// 分析进度变化事件参数
/// </summary>
public class AnalysisProgressEventArgs : EventArgs
{
    /// <summary>
    /// 当前工作的分析师名称
    /// </summary>
    public string CurrentAnalyst { get; set; } = string.Empty;

    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// 当前阶段描述
    /// </summary>
    public string StageDescription { get; set; } = string.Empty;
}
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
    /// 当前阶段描述
    /// </summary>
    public string StageDescription { get; set; } = string.Empty;

    /// <summary>
    /// 是否正在进行中
    /// </summary>
    public bool IsInProgress { get; set; } = true;
}
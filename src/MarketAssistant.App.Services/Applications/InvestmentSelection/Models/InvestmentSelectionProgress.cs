namespace MarketAssistant.Applications.InvestmentSelection.Models;

/// <summary>
/// 选股工作流的三个阶段，与工作流三步一一对应。
/// </summary>
public enum InvestmentSelectionStep
{
    /// <summary>理解需求或新闻，生成筛选条件</summary>
    GenerateCriteria,

    /// <summary>按筛选条件取得候选标的</summary>
    ScreenTargets,

    /// <summary>AI 深度分析候选标的并排序</summary>
    AnalyzeAssets
}

/// <summary>
/// 阶段状态；尚未收到事件的阶段即为待开始。
/// </summary>
public enum InvestmentSelectionStepStatus
{
    Running,
    Completed
}

/// <summary>
/// 选股进度事件：某阶段开始或完成，完成时可带一句结果摘要（如“候选 48 个标的”）。
/// </summary>
public sealed record InvestmentSelectionProgress(
    InvestmentSelectionStep Step,
    InvestmentSelectionStepStatus Status,
    string? Detail = null);

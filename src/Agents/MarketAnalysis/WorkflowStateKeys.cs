namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场分析工作流的状态键常量
/// 用于在 Workflow State 中存储和读取共享数据
/// </summary>
internal static class WorkflowStateKeys
{
    /// <summary>
    /// 工作流状态的作用域（确保不同 Executor 访问相同的状态空间）
    /// </summary>
    public const string Scope = "MarketAnalysisWorkflow";

    /// <summary>
    /// 股票代码的状态键
    /// </summary>
    public const string StockSymbol = nameof(StockSymbol);

    /// <summary>
    /// 预期分析师数量的状态键
    /// </summary>
    public const string ExpectedAnalystCount = nameof(ExpectedAnalystCount);
}



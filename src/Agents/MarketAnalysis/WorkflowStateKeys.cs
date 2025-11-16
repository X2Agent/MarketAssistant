namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场分析工作流的状态键常量
/// 用于在 Workflow State 中存储和读取共享数据
/// </summary>
internal static class WorkflowStateKeys
{
    /// <summary>
    /// 股票代码的状态键
    /// </summary>
    public const string StockSymbol = "stockSymbol";

    /// <summary>
    /// 预期分析师数量的状态键
    /// </summary>
    public const string ExpectedAnalystCount = "expectedAnalystCount";
}



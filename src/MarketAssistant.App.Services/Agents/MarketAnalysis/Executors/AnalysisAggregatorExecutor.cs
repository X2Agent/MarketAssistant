using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析聚合器 Executor（基于官方 Fan-In 模式）
/// 参考: https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow
/// 
/// Fan-In 工作原理：
/// 1. HandleAsync 会被多次调用（每个源 Agent 一次）
/// 2. 每次接收该 Agent 的消息列表（List<ChatMessage>）
/// 3. 内部维护列表收集所有消息
/// 4. 收齐后使用 context.YieldOutputAsync 输出给下游
/// 
/// </summary>
public sealed partial class AnalysisAggregatorExecutor : Executor
{
    private readonly ILogger<AnalysisAggregatorExecutor> _logger;

    public AnalysisAggregatorExecutor(
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [MessageHandler]
    private async ValueTask<List<ChatMessage>> HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // AddFanInBarrierEdge 会在上游全部完成后，将聚合后的消息列表一次性传入。
        var expectedCount = await context.ReadStateAsync<int>(
            WorkflowStateKeys.ExpectedAnalystCount,
            WorkflowStateKeys.Scope,
            cancellationToken);

        _logger.LogInformation(
            "已收集 {Expected} 位分析师的结果，共 {TotalMessages} 条消息（Context Hash: {ContextHash}）",
            expectedCount, messages.Count, context.GetHashCode());

        return messages;
    }
}

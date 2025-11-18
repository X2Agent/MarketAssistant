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
/// 注意：使用 Executor<TInput> 而不是 Executor<TInput, TOutput>
/// </summary>
public sealed class AnalysisAggregatorExecutor : Executor<List<ChatMessage>, List<ChatMessage>>
{
    private readonly List<ChatMessage> _collectedMessages = [];
    private int _receivedCount = 0;
    private readonly ILogger<AnalysisAggregatorExecutor> _logger;

    public AnalysisAggregatorExecutor(
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 接收单个分析师的消息列表（会被调用多次）
    /// 收集完所有分析师消息后，使用 YieldOutputAsync 传递给 Coordinator
    /// </summary>
    public override async ValueTask<List<ChatMessage>> HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // 每次调用表示收到一个分析师的结果
        _receivedCount++;

        _logger.LogDebug(
            "收到分析师消息 {Received} 个，消息数: {MessageCount}",
            _receivedCount, messages.Count);

        // 收集消息
        _collectedMessages.AddRange(messages);

        // 从 state 读取期望的分析师数量
        var expectedCount = await context.ReadStateAsync<int>(
            WorkflowStateKeys.ExpectedAnalystCount,
            WorkflowStateKeys.Scope,
            cancellationToken);

        _logger.LogInformation(
            "已收集 {Current}/{Expected} 位分析师的结果，共 {TotalMessages} 条消息（Context Hash: {ContextHash}）",
            _receivedCount, expectedCount, _collectedMessages.Count, context.GetHashCode());

        // 判断是否收齐了所有分析师的消息（通过调用次数判断）
        if (_receivedCount >= expectedCount)
        {
            _logger.LogInformation(
                "所有 {Count} 位分析师的结果已收集完成（共 {Total} 条消息），准备传递给 Coordinator",
                _receivedCount, _collectedMessages.Count);

            return _collectedMessages;
        }
        return null;
    }
}

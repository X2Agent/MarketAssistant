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
/// 2. 每次接收一个 ChatMessage（不是集合）
/// 3. 内部维护列表收集所有消息
/// 4. 收齐后返回结果给下游
/// </summary>
public sealed class AnalysisAggregatorExecutor : Executor<ChatMessage, List<ChatMessage>>
{
    private readonly List<ChatMessage> _collectedMessages = [];
    private readonly ILogger<AnalysisAggregatorExecutor> _logger;

    public AnalysisAggregatorExecutor(
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 接收单个分析师消息（会被调用多次）
    /// 收集完所有分析师消息后，返回列表给 Coordinator
    /// </summary>
    public override async ValueTask<List<ChatMessage>> HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogDebug(
            "收到分析师消息：{Author}, 当前已收集 {Current} 条",
            message.AuthorName, _collectedMessages.Count);

        // 收集消息
        _collectedMessages.Add(message);

        // 从 state 读取期望的分析师数量
        var expectedCount = await context.ReadStateAsync<int>(WorkflowStateKeys.ExpectedAnalystCount, cancellationToken);

        _logger.LogInformation(
            "已收集 {Current}/{Expected} 位分析师的结果",
            _collectedMessages.Count, expectedCount);

        // 判断是否收齐了所有分析师的消息
        if (_collectedMessages.Count >= expectedCount)
        {
            _logger.LogInformation(
                "所有 {Count} 位分析师的结果已收集完成，准备传递给 Coordinator",
                _collectedMessages.Count);

            // 返回收集到的所有消息
            return _collectedMessages;
        }

        // 还没收齐，返回空列表（框架会继续等待）
        // 注意：这里的返回值不会传递给下游，只是占位
        return [];
    }
}

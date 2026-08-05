using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析聚合器 Executor。
/// Fan-In barrier 会在各源至少产生一条消息后开始逐条转发，
/// 因此这里负责过滤初始 user 消息、按分析师收集最终文本，并在收齐后只发送一次。
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
public sealed partial class AnalysisAggregatorExecutor : Executor
{
    private const string CoordinatorExecutorId = "Coordinator";

    private readonly ILogger<AnalysisAggregatorExecutor> _logger;
    private readonly object _syncRoot = new();
    private readonly List<ChatMessage> _analystMessages = [];
    private bool _sentToCoordinator;

    public AnalysisAggregatorExecutor(
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [MessageHandler]
    private async ValueTask HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var expectedCount = await context.ReadStateAsync<int>(
            WorkflowStateKeys.ExpectedAnalystCount,
            WorkflowStateKeys.Scope,
            cancellationToken);

        List<ChatMessage>? readyMessages = null;
        lock (_syncRoot)
        {
            foreach (var message in messages.Where(IsAnalystTextMessage))
            {
                var normalized = new ChatMessage(ChatRole.Assistant, message.Text!)
                {
                    AuthorName = message.AuthorName
                };

                var author = message.AuthorName;
                var existingIndex = author == null
                    ? -1
                    : _analystMessages.FindIndex(existing =>
                        string.Equals(existing.AuthorName, author, StringComparison.Ordinal));

                if (existingIndex >= 0)
                    _analystMessages[existingIndex] = normalized;
                else if (author != null || _analystMessages.Count < expectedCount)
                    _analystMessages.Add(normalized);
            }

            var collectedCount = _analystMessages.Count;
            _logger.LogInformation(
                "分析师汇聚收到消息，本批: {BatchCount}，有效分析师文本: {CollectedCount}/{ExpectedCount}，已发送: {Sent}",
                messages.Count,
                collectedCount,
                expectedCount,
                _sentToCoordinator);

            if (!_sentToCoordinator && expectedCount > 0 && collectedCount >= expectedCount)
            {
                readyMessages = [.. _analystMessages];
                _sentToCoordinator = true;
            }
        }

        if (readyMessages == null)
            return;

        LogMessageDiagnostics(readyMessages);
        await context.SendMessageAsync(
            readyMessages,
            CoordinatorExecutorId,
            cancellationToken);

        _logger.LogInformation(
            "分析师已收齐，发送 {Count} 条最终文本给 Coordinator",
            readyMessages.Count);
    }

    private static bool IsAnalystTextMessage(ChatMessage message)
        => message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text);

    private void LogMessageDiagnostics(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            _logger.LogInformation(
                "分析师最终消息 [{Index}/{Count}] Author: {Author}, TextLength: {TextLength}, ContentTypes: [{ContentTypes}]",
                index + 1,
                messages.Count,
                message.AuthorName ?? "null",
                message.Text?.Length ?? 0,
                string.Join(", ", message.Contents.Select(content => content.GetType().Name)));
        }
    }
}

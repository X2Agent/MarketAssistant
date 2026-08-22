using MarketAssistant.Agents.Analysts;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.MarketAnalysis.Executors;

/// <summary>
/// 分析聚合器 Executor。
/// Fan-In barrier 会在各源至少产生一条消息后开始逐条转发，
/// 因此这里负责过滤初始 user 消息、按分析师收集最终文本，并在收齐后只发送一次。
/// 失败隔离包装器（<see cref="AIAgentFailureIsolation"/>）保证每位分析师
/// （含失败者）恰好产出一条消息：失败者以 <see cref="AnalystFailureMessages"/>
/// 标记消息到达。这里把失败消息排除出协调分析师载荷，改附维度缺失说明。
/// 全部失败时不派发 Coordinator（由工作流终局诊断给出明确错误）——
/// Fan-In barrier 目标执行器抛出的异常不会以 ExecutorFailedEvent 暴露，不能依赖抛错中断流程。
/// </summary>
[SendsMessage(typeof(List<ChatMessage>))]
public sealed partial class AnalysisAggregatorExecutor : Executor
{
    private const string CoordinatorExecutorId = "Coordinator";
    private const string SystemNoticeAuthorName = "SystemNotice";

    private readonly ILogger<AnalysisAggregatorExecutor> _logger;
    private readonly int _expectedAnalystCount;
    private readonly object _syncRoot = new();
    private readonly List<ChatMessage> _analystMessages = [];
    private readonly List<ChatMessage> _failedMessages = [];
    private bool _sentToCoordinator;

    /// <param name="expectedAnalystCount">期望的分析师消息总数（成功 + 失败标记）。
    /// 由工作流在构建时直接传入，避免经 Workflow State 传递带来的跨 SuperStep 时序竞态。</param>
    public AnalysisAggregatorExecutor(
        int expectedAnalystCount,
        ILogger<AnalysisAggregatorExecutor> logger)
        : base(id: "AnalysisAggregator")
    {
        _expectedAnalystCount = expectedAnalystCount;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [MessageHandler]
    private async ValueTask HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var expectedCount = _expectedAnalystCount;

        List<ChatMessage>? readyMessages = null;
        lock (_syncRoot)
        {
            foreach (var message in messages)
            {
                if (!IsAnalystTextMessage(message))
                {
                    _logger.LogDebug(
                        "聚合器忽略非分析师文本消息: Role: {Role}, Author: {Author}, TextLength: {TextLength}, ContentTypes: [{ContentTypes}]",
                        message.Role,
                        message.AuthorName ?? "null",
                        message.Text?.Length ?? 0,
                        string.Join(", ", message.Contents.Select(content => content.GetType().Name)));
                    continue;
                }

                if (AnalystFailureMessages.IsFailureMarker(message.Text))
                {
                    CollectMessage(_failedMessages, message, expectedCount);
                    continue;
                }

                CollectMessage(_analystMessages, message, expectedCount);
            }

            var collectedCount = _analystMessages.Count;
            var failedCount = _failedMessages.Count;
            _logger.LogInformation(
                "分析师汇聚收到消息，本批: {BatchCount}，有效分析师文本: {CollectedCount}，失败: {FailedCount}，期望: {ExpectedCount}，已发送: {Sent}",
                messages.Count,
                collectedCount,
                failedCount,
                expectedCount,
                _sentToCoordinator);

            if (_sentToCoordinator || expectedCount <= 0 || collectedCount + failedCount < expectedCount)
                return;

            _sentToCoordinator = true;

            if (collectedCount == 0)
            {
                // 全部失败：不派发 Coordinator（报告无从生成），
                // 工作流事件流自然结束，由 AnalyzeAsync 终局诊断给出明确错误
                _logger.LogError("聚合器判定所有分析师均执行失败，不派发协调分析师: {Failures}", FormatFailures());
                return;
            }

            readyMessages =
            [
                .. _analystMessages,
                new ChatMessage(ChatRole.Assistant, BuildMissingDimensionNote())
                {
                    AuthorName = SystemNoticeAuthorName
                }
            ];
        }

        if (readyMessages == null)
            return;

        LogMessageDiagnostics(readyMessages);
        await context.SendMessageAsync(
            readyMessages,
            CoordinatorExecutorId,
            cancellationToken);

        _logger.LogInformation(
            "分析师已收齐（{SuccessCount} 成功 / {FailedCount} 失败），发送 {Count} 条最终文本给 Coordinator",
            _analystMessages.Count,
            _failedMessages.Count,
            readyMessages.Count);
    }

    /// <summary>
    /// 按作者去重收集消息：同一作者的重复消息以最后一条为准；
    /// 无作者消息仅在未超出期望数量时兜底收集。
    /// </summary>
    private static void CollectMessage(List<ChatMessage> target, ChatMessage message, int expectedCount)
    {
        var normalized = new ChatMessage(ChatRole.Assistant, message.Text!)
        {
            AuthorName = message.AuthorName
        };

        var author = message.AuthorName;
        var existingIndex = author == null
            ? -1
            : target.FindIndex(existing =>
                string.Equals(existing.AuthorName, author, StringComparison.Ordinal));

        if (existingIndex >= 0)
            target[existingIndex] = normalized;
        else if (author != null || target.Count < expectedCount)
            target.Add(normalized);
    }

    private string FormatFailures()
        => string.Join("；", _failedMessages.Select(message =>
            $"{message.AuthorName ?? "未知分析师"}({ExtractFailureReason(message.Text)})"));

    private static string ExtractFailureReason(string? markerText)
    {
        if (string.IsNullOrEmpty(markerText))
            return "未知错误";

        // 标记格式见 AnalystFailureMessages.BuildFailureText：前缀 + 空格 + "AgentName: reason"。
        // 流式失败时标记前可能拼接了部分正文，故先定位标记再取其后的 "AgentName: reason" 段。
        var markerIndex = markerText.IndexOf(AnalystFailureMessages.FailureMarkerPrefix, StringComparison.Ordinal);
        if (markerIndex < 0)
            return markerText;

        var afterMarker = markerText[(markerIndex + AnalystFailureMessages.FailureMarkerPrefix.Length)..].TrimStart();
        var separatorIndex = afterMarker.IndexOf(": ", StringComparison.Ordinal);
        return separatorIndex >= 0 ? afterMarker[(separatorIndex + 2)..] : afterMarker;
    }

    private string BuildMissingDimensionNote()
        => AnalystFailureMessages.BuildMissingDimensionNote(
            _failedMessages.Select(message =>
                $"{message.AuthorName ?? "未知分析师"}（{ExtractFailureReason(message.Text)}）").ToList());

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

using MarketAssistant.Services.Agents.MarketAnalysis.Artifacts;
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

    /// <summary>消息未收齐时的默认降级宽限期（P0-1 缓解：避免事件流无声终止）。</summary>
    private static readonly TimeSpan DefaultIncompleteGrace = TimeSpan.FromSeconds(30);

    private readonly ILogger<AnalysisAggregatorExecutor> _logger;
    private readonly int _expectedAnalystCount;
    private readonly Guid _runId;
    private readonly IAnalystArtifactStore _artifactStore;
    private readonly IReadOnlyDictionary<string, string> _displayNameByAgentName;
    private readonly TimeSpan _incompleteGrace;
    private readonly object _syncRoot = new();
    private readonly List<ChatMessage> _analystMessages = [];
    private readonly List<ChatMessage> _failedMessages = [];
    private bool _sentToCoordinator;
    private bool _degradeSendScheduled;

    /// <param name="expectedAnalystCount">期望的分析师消息总数（成功 + 失败标记）。
    /// 由工作流在构建时直接传入，避免经 Workflow State 传递带来的跨 SuperStep 时序竞态。</param>
    /// <param name="runId">本次分析 Run 的 ID（产物存储按 Run 隔离）。</param>
    /// <param name="artifactStore">分析师产物存储（P1-07：全文落盘，协调只传摘要）。</param>
    /// <param name="displayNameByAgentName">Agent ASCII Name → 中文显示名映射（用于摘要可读性），可为空字典。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="incompleteGracePeriod">消息未收齐时的降级宽限期，超时后降级发送已有结果；为 null 时使用默认值。</param>
    public AnalysisAggregatorExecutor(
        int expectedAnalystCount,
        Guid runId,
        IAnalystArtifactStore artifactStore,
        IReadOnlyDictionary<string, string>? displayNameByAgentName,
        ILogger<AnalysisAggregatorExecutor> logger,
        TimeSpan? incompleteGracePeriod = null)
        : base(id: "AnalysisAggregator")
    {
        _expectedAnalystCount = expectedAnalystCount;
        _runId = runId;
        _artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        _displayNameByAgentName = displayNameByAgentName ?? new Dictionary<string, string>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _incompleteGrace = incompleteGracePeriod ?? DefaultIncompleteGrace;
    }

    [MessageHandler]
    private async ValueTask HandleAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var expectedCount = _expectedAnalystCount;

        List<ChatMessage>? readySuccesses = null;
        List<ChatMessage>? readyFailures = null;
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

            if (_sentToCoordinator || expectedCount <= 0)
                return;

            if (collectedCount + failedCount < expectedCount)
            {
                // Fan-In barrier 缺陷缓解（P0-1）：消息未收齐时不无限静默等待，
                // 启动有界宽限计时，到期后降级发送已有结果
                ScheduleDegradeSend(context);
                return;
            }

            if (collectedCount == 0)
            {
                // 全部失败：不派发 Coordinator（报告无从生成），
                // 工作流事件流自然结束，由 AnalyzeAsync 终局诊断给出明确错误
                _logger.LogError("聚合器判定所有分析师均执行失败，不派发协调分析师: {Failures}", FormatFailures());
                return;
            }

            _sentToCoordinator = true;
            readySuccesses = [.. _analystMessages];
            readyFailures = [.. _failedMessages];
        }

        LogMessageDiagnostics(readySuccesses!);
        var payload = await BuildCoordinatorPayloadAsync(
            readySuccesses!, readyFailures!, degraded: false, cancellationToken);
        await context.SendMessageAsync(payload, CoordinatorExecutorId, cancellationToken);

        _logger.LogInformation(
            "分析师已收齐（{SuccessCount} 成功 / {FailedCount} 失败），发送 {Count} 条最终文本给 Coordinator",
            readySuccesses!.Count,
            readyFailures!.Count,
            payload.Count);
    }

    /// <summary>
    /// 启动一次性的降级发送计时：宽限期内若正常路径仍未收齐，
    /// 则将已收到的成功结果（附降级说明）发送给 Coordinator，避免事件流无声终止。
    /// </summary>
    private void ScheduleDegradeSend(IWorkflowContext context)
    {
        lock (_syncRoot)
        {
            if (_degradeSendScheduled || _sentToCoordinator)
                return;
            _degradeSendScheduled = true;
        }

        _logger.LogWarning(
            "分析师消息未收齐（{Collected} 成功 / {Failed} 失败 / 期望 {Expected}），将在 {Grace.TotalSeconds}s 后降级发送已有结果",
            _analystMessages.Count,
            _failedMessages.Count,
            _expectedAnalystCount,
            _incompleteGrace);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_incompleteGrace).ConfigureAwait(false);

                List<ChatMessage> successes;
                List<ChatMessage> failures;
                lock (_syncRoot)
                {
                    if (_sentToCoordinator || _analystMessages.Count == 0)
                        return; // 正常路径已完成，或无任何成功结果可降级
                    _sentToCoordinator = true;
                    successes = [.. _analystMessages];
                    failures = [.. _failedMessages];
                }

                var payload = await BuildCoordinatorPayloadAsync(
                    successes, failures, degraded: true, CancellationToken.None).ConfigureAwait(false);
                await context.SendMessageAsync(
                    payload, CoordinatorExecutorId, CancellationToken.None).ConfigureAwait(false);

                _logger.LogWarning(
                    "宽限期到，已降级发送：{SuccessCount} 成功 / {FailedCount} 失败 / 期望 {Expected}",
                    successes.Count, failures.Count, _expectedAnalystCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "聚合器降级发送失败");
            }
        });
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

    /// <summary>
    /// 构建发给 Coordinator 的载荷：降级说明（仅降级路径）+ 系统指引 + 各分析师摘要（全文已落盘，通过工具读取）+ 维度缺失说明。
    /// 落盘失败的条目自动回退为全文注入。
    /// </summary>
    private async Task<List<ChatMessage>> BuildCoordinatorPayloadAsync(
        IReadOnlyList<ChatMessage> successes,
        IReadOnlyList<ChatMessage> failures,
        bool degraded,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();

        if (degraded)
        {
            messages.Add(new ChatMessage(ChatRole.System, BuildDegradedNotice(successes.Count + failures.Count))
            {
                AuthorName = SystemNoticeAuthorName
            });
        }

        messages.Add(new ChatMessage(ChatRole.System, BuildArtifactInstructionNote())
        {
            AuthorName = SystemNoticeAuthorName
        });

        foreach (var success in successes)
        {
            var persisted = await TryPersistArtifactAsync(success, cancellationToken).ConfigureAwait(false);
            messages.Add(persisted
                ? BuildSummaryMessage(success)
                : success); // 回退：全文注入
        }

        if (failures.Count > 0)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, BuildMissingDimensionNote(failures))
            {
                AuthorName = SystemNoticeAuthorName
            });
        }

        return messages;
    }

    /// <summary>
    /// 尝试将产物写入存储；返回是否成功。
    /// </summary>
    private async Task<bool> TryPersistArtifactAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.AuthorName))
            return false; // 无作者名无法定位产物，回退全文

        try
        {
            await _artifactStore.SaveAsync(_runId, message.AuthorName, message.Text!, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 落盘失败时回退为全文传递，保证协调阶段仍有完整信息可用
            _logger.LogError(ex, "产物落盘失败: Run: {RunId}, Analyst: {Analyst}，将回退为全文注入",
                _runId.ToString("N"), message.AuthorName);
            return false;
        }
    }

    /// <summary>
    /// 将成功消息压缩为摘要：显示名、字符数与工具读取指引。
    /// </summary>
    private ChatMessage BuildSummaryMessage(ChatMessage message)
    {
        var author = message.AuthorName ?? "未知分析师";
        var displayName = _displayNameByAgentName.GetValueOrDefault(author, author);
        var runIdText = _runId.ToString("N");

        var summary =
            $"【{displayName}】结论摘要：共 {message.Text!.Length} 字符。" +
            $"请通过工具 get_analyst_artifact 读取全文后再引用细节" +
            $"（参数：runId=\"{runIdText}\"，analystName=\"{author}\"）。禁止凭摘要编造具体数值或结论。";

        return new ChatMessage(ChatRole.Assistant, summary)
        {
            AuthorName = message.AuthorName
        };
    }

    /// <summary>
    /// 系统级说明：告知协调器产物读取方式与禁止事项。
    /// </summary>
    private string BuildArtifactInstructionNote()
        => "以下为各分析师的结论摘要。每条摘要对应的完整产物已保存，" +
           $"本次运行 ID 为 {_runId:N}。需要任何维度的具体数据时，必须调用 get_analyst_artifact 工具读取全文；" +
           "严禁仅依据摘要编造数值、评级或细节。";

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

    /// <summary>
    /// 降级说明：告知协调器本次结果不完整，需在报告中注明。
    /// </summary>
    private string BuildDegradedNotice(int receivedCount)
        => $"【系统提示】本次运行期望 {_expectedAnalystCount} 位分析师的结果，超时仅收到 {receivedCount} 份。" +
           "以下为已收到维度的分析结论，报告必须注明分析维度不完整、结论可能存在偏差。";

    private string BuildMissingDimensionNote(IReadOnlyList<ChatMessage> failedMessages)
        => AnalystFailureMessages.BuildMissingDimensionNote(
            failedMessages.Select(message =>
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

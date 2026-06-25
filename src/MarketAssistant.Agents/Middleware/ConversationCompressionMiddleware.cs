using MarketAssistant.Agents.TokenManagement;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace MarketAssistant.Agents.Middleware;

/// <summary>
/// 会话压缩中间件，拦截 Agent 运行并在消息历史超过 Token 阈值时自动压缩。
/// 通过 agent.AsBuilder().Use(runFunc:, runStreamingFunc:).Build() 附加。
/// </summary>
public sealed class ConversationCompressionMiddleware
{
    /// <summary>
    /// AgentSession.StateBag 中标记是否正在执行压缩的键（防止递归）
    /// </summary>
    private const string IsCompressingKey = "middleware:isCompressing";

    /// <summary>
    /// AgentSession.StateBag 中存储压缩摘要的键
    /// </summary>
    public const string CompressionSummaryKey = "middleware:compressionSummary";

    private const int DefaultMaxTokens = 8000;
    private const int DefaultReserveRecentCount = 4;
    private const int SummaryTextTruncationThreshold = 500;
    private const int SummaryMaxOutputTokens = 500;
    private const float SummaryTemperature = 0.1f;
    private const int FallbackSummaryTruncationThreshold = 100;

    private readonly ILogger _logger;
    private readonly Func<IChatClient> _chatClientFactory;

    /// <summary>
    /// 压缩前回调钩子。在丢弃旧消息前调用，允许外部提取关键信息（紧急保存）。
    /// </summary>
    public Func<IList<ChatMessage>, CancellationToken, Task>? PreCompressHook { get; set; }

    /// <summary>
    /// 触发压缩的 Token 阈值
    /// </summary>
    public int MaxTokens { get; set; } = DefaultMaxTokens;

    /// <summary>
    /// 压缩时保留最近消息数
    /// </summary>
    public int ReserveRecentCount { get; set; } = DefaultReserveRecentCount;

    public ConversationCompressionMiddleware(Func<IChatClient> chatClientFactory, ILogger<ConversationCompressionMiddleware> logger)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 非流式 Agent Run 中间件
    /// </summary>
    public async Task<AgentResponse> InvokeAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        var processedMessages = await TryCompressAsync(messages, session, cancellationToken);

        return await innerAgent.RunAsync(processedMessages, session, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 流式 Agent Run 中间件
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> InvokeStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processedMessages = await TryCompressAsync(messages, session, cancellationToken);

        await foreach (var update in innerAgent.RunStreamingAsync(processedMessages, session, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <summary>
    /// 检查消息列表是否需要压缩，如需要则执行压缩并返回处理后的消息
    /// </summary>
    private async Task<IEnumerable<ChatMessage>> TryCompressAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        CancellationToken cancellationToken)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();

        // 防止递归：压缩摘要生成过程中不再触发压缩
        if (session?.StateBag.TryGetValue<string>(IsCompressingKey, out var flag) == true && flag == "true")
        {
            return messageList;
        }

        var totalTokens = TokenEstimator.EstimateTotalTokens(messageList);
        if (totalTokens <= MaxTokens || messageList.Count <= ReserveRecentCount)
        {
            return messageList;
        }

        _logger.LogInformation(
            "消息 Token ({Tokens}) 超过阈值 ({Max})，触发压缩，消息数: {Count}",
            totalTokens, MaxTokens, messageList.Count);

        // 压缩前紧急保存钩子：让外部提取关键信息后再丢弃旧消息
        if (PreCompressHook != null)
        {
            try
            {
                await PreCompressHook(messageList, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "压缩前紧急保存钩子执行失败");
            }
        }

        var messagesToSummarize = messageList.Take(messageList.Count - ReserveRecentCount).ToList();
        var recentMessages = messageList.Skip(messageList.Count - ReserveRecentCount).ToList();

        var summary = await GenerateSummaryAsync(messagesToSummarize, session, cancellationToken);

        var compressed = new List<ChatMessage>(ReserveRecentCount + 1)
        {
            new(ChatRole.System, $"[对话摘要] {summary}")
        };
        compressed.AddRange(recentMessages);

        // 将摘要存到 StateBag 以便外部（如 UI）读取
        if (session != null)
        {
            session.StateBag.SetValue(CompressionSummaryKey, summary);
        }

        var newTokens = TokenEstimator.EstimateTotalTokens(compressed);
        _logger.LogInformation(
            "压缩完成：{OldCount} → {NewCount} 条消息，Token: {OldTokens} → {NewTokens}",
            messageList.Count, compressed.Count, totalTokens, newTokens);

        return compressed;
    }

    /// <summary>
    /// 使用 LLM 生成对话摘要（设置防递归标记）
    /// </summary>
    private async Task<string> GenerateSummaryAsync(
        List<ChatMessage> messages,
        AgentSession? session,
        CancellationToken cancellationToken)
    {
        // 设置防递归标记
        if (session != null) session.StateBag.SetValue(IsCompressingKey, "true");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("请将以下对话内容压缩为简洁摘要，保留关键结论和数据点：");
            sb.AppendLine();

            foreach (var msg in messages)
            {
                var role = msg.Role == ChatRole.User ? "用户" : "助手";
                var text = msg.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (text.Length > SummaryTextTruncationThreshold)
                    text = text[..SummaryTextTruncationThreshold] + "...";

                sb.AppendLine($"【{role}】{text}");
            }

            var response = await _chatClientFactory().GetResponseAsync(
                [new ChatMessage(ChatRole.User, sb.ToString())],
                new ChatOptions { Temperature = SummaryTemperature, MaxOutputTokens = SummaryMaxOutputTokens },
                cancellationToken);

            return response.Text ?? "对话历史摘要不可用";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 摘要生成失败，回退到截断策略");
            return BuildFallbackSummary(messages);
        }
        finally
        {
            if (session != null) session.StateBag.TryRemoveValue(IsCompressingKey);
        }
    }

    /// <summary>
    /// 回退摘要策略：提取每条消息的前 100 字符
    /// </summary>
    private static string BuildFallbackSummary(List<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var text = msg.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var truncated = text.Length > FallbackSummaryTruncationThreshold ? text[..FallbackSummaryTruncationThreshold] + "..." : text;
            sb.AppendLine($"- {truncated}");
        }
        return sb.ToString();
    }
}

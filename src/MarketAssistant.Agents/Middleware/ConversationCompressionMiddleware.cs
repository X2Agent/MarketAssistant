using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Middleware;

/// <summary>
/// 为每个聊天会话创建独立的 MAF Compaction Provider。
/// 官方 Provider 按原子消息组处理历史，保证 Function Call 与 Function Result 不被拆分。
/// </summary>
public sealed class ConversationCompactionProviderFactory
{
    public const int DefaultMaxTokens = 8_000;
    public const double ContextWindowTriggerRatio = 0.75;
    public const int DefaultMinimumPreservedGroups = 8;
    public const string StateKey = "market-chat:compaction";

    private readonly ILoggerFactory _loggerFactory;

    public ConversationCompactionProviderFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public AIContextProvider Create(
        IChatClient chatClient,
        int maxTokens = DefaultMaxTokens,
        int minimumPreservedGroups = DefaultMinimumPreservedGroups)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumPreservedGroups);

        var strategy = new SummarizationCompactionStrategy(
            chatClient,
            CompactionTriggers.TokensExceed(maxTokens),
            minimumPreservedGroups);

        return new CompactionProvider(strategy, StateKey, _loggerFactory);
    }

    /// <summary>
    /// 根据模型上下文窗口创建 Provider。未知窗口使用保守默认阈值；已知窗口在 75% 时触发压缩，
    /// 为系统提示、工具调用和模型输出保留至少 25% 的空间。
    /// </summary>
    public AIContextProvider CreateForContextWindow(
        IChatClient chatClient,
        int? contextWindowTokens,
        int minimumPreservedGroups = DefaultMinimumPreservedGroups)
    {
        return Create(
            chatClient,
            CalculateMaxTokens(contextWindowTokens),
            minimumPreservedGroups);
    }

    public static int CalculateMaxTokens(int? contextWindowTokens)
    {
        if (contextWindowTokens is null)
            return DefaultMaxTokens;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextWindowTokens.Value);

        var calculatedThreshold = (long)Math.Floor(contextWindowTokens.Value * ContextWindowTriggerRatio);
        return checked((int)Math.Clamp(calculatedThreshold, 1, int.MaxValue));
    }
}

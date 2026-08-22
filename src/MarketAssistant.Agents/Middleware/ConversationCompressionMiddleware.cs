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
    /// <summary>
    /// 统一压缩阈值。按主流 128K 上下文档位设定；真实窗口更小的模型在超长对话中可能先触发
    /// API 上下文超限，属已知取舍。
    /// </summary>
    public const int DefaultMaxTokens = 128_000;
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
}

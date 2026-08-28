using MarketAssistant.Infrastructure.Tokenization;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// Token 估算器（静态门面）：基于 <see cref="TiktokenTokenCounter"/>（cl100k_base）精确计算 Token 数。
/// 词表由 ITokenCounter 实现统一延迟加载一次；后续可逐步改为构造注入 ITokenCounter
/// （TokenTrackingMiddleware/MarketChatSession 目前由测试手工构造，故暂时保留静态门面）。
/// </summary>
public static class TokenEstimator
{
    private static readonly ITokenCounter Counter = new TiktokenTokenCounter();

    /// <summary>
    /// 估算单条消息的 Token 数
    /// </summary>
    public static int EstimateTokens(ChatMessage message)
    {
        var text = message.Text ?? string.Empty;
        return EstimateTokens(text);
    }

    /// <summary>
    /// 估算文本的 Token 数
    /// </summary>
    public static int EstimateTokens(string text)
        => Counter.CountTokens(text);

    /// <summary>
    /// 估算对话历史的总 Token 数
    /// </summary>
    public static int EstimateTotalTokens(IEnumerable<ChatMessage> messages)
    {
        return messages.Sum(EstimateTokens);
    }
}

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// 对话压缩器，当会话 Token 超过阈值时自动压缩历史消息
/// 策略：保留最新 N 条消息，将更早的消息压缩为摘要
/// </summary>
public class ConversationCompressor
{
    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;

    private const int DefaultMaxTokens = 8000;
    private const int DefaultReserveRecentCount = 4;

    public int MaxTokens { get; set; } = DefaultMaxTokens;
    public int ReserveRecentCount { get; set; } = DefaultReserveRecentCount;

    public ConversationCompressor(IChatClient chatClient, ILogger logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// 检查是否需要压缩
    /// </summary>
    public bool NeedsCompression(IReadOnlyList<ChatMessage> history)
    {
        return TokenEstimator.EstimateTotalTokens(history) > MaxTokens;
    }

    /// <summary>
    /// 压缩对话历史：将旧消息总结为摘要，保留最近的消息
    /// </summary>
    public async Task<List<ChatMessage>> CompressAsync(
        IReadOnlyList<ChatMessage> history,
        string? analysisContext = null,
        CancellationToken cancellationToken = default)
    {
        if (history.Count <= ReserveRecentCount)
            return [.. history];

        var totalTokens = TokenEstimator.EstimateTotalTokens(history);
        _logger.LogInformation(
            "开始压缩对话历史，当前消息数: {Count}，估算 Token: {Tokens}",
            history.Count, totalTokens);

        var messagesToSummarize = history.Take(history.Count - ReserveRecentCount).ToList();
        var recentMessages = history.Skip(history.Count - ReserveRecentCount).ToList();

        var summary = await GenerateSummaryAsync(messagesToSummarize, cancellationToken);

        var compressed = new List<ChatMessage>
        {
            new(ChatRole.System, $"[对话摘要] {summary}")
        };
        compressed.AddRange(recentMessages);

        var newTokens = TokenEstimator.EstimateTotalTokens(compressed);
        _logger.LogInformation(
            "对话压缩完成，{OldCount} 条消息 → {NewCount} 条，Token: {OldTokens} → {NewTokens}",
            history.Count, compressed.Count, totalTokens, newTokens);

        return compressed;
    }

    /// <summary>
    /// 使用 LLM 生成对话摘要
    /// </summary>
    private async Task<string> GenerateSummaryAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("请将以下对话内容压缩为简洁摘要，保留关键结论和数据点：");
        sb.AppendLine();

        foreach (var msg in messages)
        {
            var role = msg.Role == ChatRole.User ? "用户" : "助手";
            var text = msg.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (text.Length > 500)
                text = text[..500] + "...";

            sb.AppendLine($"【{role}】{text}");
        }

        try
        {
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, sb.ToString())],
                new ChatOptions { Temperature = 0.1f, MaxOutputTokens = 500 },
                cancellationToken);

            return response.Text ?? "对话历史摘要不可用";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 摘要生成失败，回退到截断策略");
            return BuildFallbackSummary(messages);
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
            var truncated = text.Length > 100 ? text[..100] + "..." : text;
            sb.AppendLine($"- {truncated}");
        }
        return sb.ToString();
    }
}

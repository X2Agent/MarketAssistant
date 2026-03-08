using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// Token 估算器，基于字符统计估算消息的 Token 数
/// 中文约 1.5 字/token，英文约 4 字符/token，混合场景取加权平均
/// </summary>
public static class TokenEstimator
{
    private const double ChineseCharsPerToken = 1.5;
    private const double EnglishCharsPerToken = 4.0;

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
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int chineseCount = 0;
        int otherCount = 0;

        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF ||
                ch >= 0x3400 && ch <= 0x4DBF ||
                ch >= 0x20000 && ch <= 0x2A6DF ||
                ch >= 0x3000 && ch <= 0x303F ||
                ch >= 0xFF00 && ch <= 0xFFEF)
            {
                chineseCount++;
            }
            else
            {
                otherCount++;
            }
        }

        var tokens = (int)(chineseCount / ChineseCharsPerToken + otherCount / EnglishCharsPerToken);
        return Math.Max(tokens, 1);
    }

    /// <summary>
    /// 估算对话历史的总 Token 数
    /// </summary>
    public static int EstimateTotalTokens(IEnumerable<ChatMessage> messages)
    {
        return messages.Sum(EstimateTokens);
    }
}

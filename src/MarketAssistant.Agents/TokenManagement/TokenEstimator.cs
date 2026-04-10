using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;

namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// Token 估算器，基于 tiktoken（cl100k_base）精确计算 Token 数。
/// 默认使用 GPT-4o 的分词模型；若初始化失败则回退到字符启发式估算。
/// </summary>
public static class TokenEstimator
{
    private static readonly Tokenizer? _tokenizer;

    static TokenEstimator()
    {
        try
        {
            // 使用 cl100k_base 编码而非绑定特定模型名，与具体 LLM 提供商无关
            _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        }
        catch
        {
            // 离线环境或编码数据不可用时回退到启发式估算
            _tokenizer = null;
        }
    }

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

        if (_tokenizer != null)
            return _tokenizer.CountTokens(text);

        return FallbackEstimate(text);
    }

    /// <summary>
    /// 估算对话历史的总 Token 数
    /// </summary>
    public static int EstimateTotalTokens(IEnumerable<ChatMessage> messages)
    {
        return messages.Sum(EstimateTokens);
    }

    /// <summary>
    /// 回退启发式估算（tiktoken 不可用时）
    /// </summary>
    private static int FallbackEstimate(string text)
    {
        int chineseCount = 0;
        int otherCount = 0;

        foreach (var ch in text)
        {
            if (ch is >= '\u4E00' and <= '\u9FFF' or
                >= '\u3400' and <= '\u4DBF' or
                >= '\u3000' and <= '\u303F' or
                >= '\uFF00' and <= '\uFFEF')
            {
                chineseCount++;
            }
            else
            {
                otherCount++;
            }
        }

        var tokens = (int)(chineseCount / 1.5 + otherCount / 4.0);
        return Math.Max(tokens, 1);
    }
}

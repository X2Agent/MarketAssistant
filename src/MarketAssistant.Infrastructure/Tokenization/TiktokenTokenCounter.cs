using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.Tokenizers;

namespace MarketAssistant.Infrastructure.Tokenization;

/// <summary>
/// 基于 Tiktoken（cl100k_base）的 Token 计数器。
/// 词表在首次使用时延迟加载一次（进程内单例注册）；
/// 离线环境或编码数据不可用时回退到中英文混合的启发式估算。
/// </summary>
public sealed class TiktokenTokenCounter : ITokenCounter
{
    private const double ChineseTokenRatio = 1.5;
    private const double OtherTokenRatio = 4.0;

    private readonly Lazy<Tokenizer?> _tokenizer;
    private readonly ILogger<TiktokenTokenCounter> _logger;

    public TiktokenTokenCounter() : this(NullLogger<TiktokenTokenCounter>.Instance)
    {
    }

    public TiktokenTokenCounter(ILogger<TiktokenTokenCounter> logger)
    {
        _logger = logger;
        // 延迟加载词表：cl100k_base 对中文分词偏保守（token 数更多），用于估算更安全；
        // 且与具体 LLM 提供商无关，适用于 DeepSeek/Qwen 等非 OpenAI 模型。
        _tokenizer = new Lazy<Tokenizer?>(() =>
        {
            try
            {
                return TiktokenTokenizer.CreateForEncoding("cl100k_base");
            }
            catch (Exception ex)
            {
                // 离线环境或编码数据不可用时回退到启发式估算。
                // Release 下 Debug.WriteLine 会被编译掉，必须用日志留下诊断痕迹，
                // 否则生产环境的 token 计数偏差完全不可见
                _logger.LogWarning(ex, "tiktoken 词表加载失败，Token 计数回退到启发式估算（精度降低）");
                return null;
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var tokenizer = _tokenizer.Value;
        if (tokenizer != null)
            return tokenizer.CountTokens(text);

        return FallbackEstimate(text);
    }

    /// <summary>
    /// 回退启发式估算（tiktoken 不可用时）：区分中文与其他字符按经验比率折算
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

        var tokens = (int)(chineseCount / ChineseTokenRatio + otherCount / OtherTokenRatio);
        return Math.Max(tokens, 1);
    }
}

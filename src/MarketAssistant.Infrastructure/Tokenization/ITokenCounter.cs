namespace MarketAssistant.Infrastructure.Tokenization;

/// <summary>
/// Token 计数统一入口：全仓所有 token 估算（分块限制、对话历史统计等）均通过此接口完成，
/// 避免各处各自维护 Tiktoken 编码器与启发式回退逻辑。
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// 计算文本的 Token 数；空文本返回 0。
    /// </summary>
    int CountTokens(string text);
}

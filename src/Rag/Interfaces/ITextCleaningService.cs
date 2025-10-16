namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 文本清洗服务接口。
/// 负责去噪、归一化、移除页眉页脚/页码、修复断词等。
/// </summary>
public interface ITextCleaningService
{
    /// <summary>
    /// 清洗单段文本。
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>清洗后的文本</returns>
    string Clean(string text);

    /// <summary>
    /// 验证清洗结果是否可接受
    /// </summary>
    /// <param name="originalText">原始文本</param>
    /// <param name="cleanedText">清洗后文本</param>
    /// <returns>清洗是否成功</returns>
    bool IsCleaningSuccessful(string originalText, string cleanedText);
}



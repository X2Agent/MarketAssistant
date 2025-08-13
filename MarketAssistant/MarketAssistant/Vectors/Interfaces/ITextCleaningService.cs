namespace MarketAssistant.Vectors.Interfaces;

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
}



namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 文档到Markdown转换器接口
/// </summary>
public interface IMarkdownConverter
{
    /// <summary>
    /// 判断是否支持转换指定类型的文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持转换</returns>
    bool CanConvert(string filePath);

    /// <summary>
    /// 将文档转换为Markdown文本
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>转换后的Markdown文本</returns>
    Task<string> ConvertToMarkdownAsync(string filePath);
}
